using FluentValidation.Results;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Requests;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.DTOs.Requests;
using HR.Modules.Platform.Services.Completion;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Requests;

public interface IRequestEffectDefinitionService
{
    Task<IReadOnlyList<RequestEffectDefinitionDto>> ListAsync(Guid requestTypeId, CancellationToken ct);
    Task<RequestEffectDefinitionDto> AddAsync(Guid requestTypeId, UpsertEffectDefinitionInput input, CancellationToken ct);
    Task<RequestEffectDefinitionDto> UpdateAsync(Guid effectId, UpsertEffectDefinitionInput input, CancellationToken ct);
    Task<RequestEffectDefinitionDto> SetEnabledAsync(Guid effectId, bool enabled, CancellationToken ct);
    Task DeleteAsync(Guid effectId, CancellationToken ct);
    Task<IReadOnlyList<RequestEffectDefinitionDto>> ReorderAsync(Guid requestTypeId, ReorderEffectsInput input, CancellationToken ct);
    Task<ValidationResultDto> ValidateAsync(Guid requestTypeId, UpsertEffectDefinitionInput input, CancellationToken ct);
}

/// <summary>
/// Effect-definition CRUD, with the required-effect protections enforced here rather than in the UI.
///
/// A required effect cannot be deleted, disabled, or have its action swapped for a different one.
/// Those are the effects other modules depend on — a Leave Request that no longer deducts balance
/// leaves payroll diverging from what HR approved — and provisioning would repair them on the next
/// pass anyway, so allowing the edit would produce a change that silently reverts.
///
/// What a tenant *may* do to a required effect is remap its inputs: a form field can legitimately be
/// renamed, and refusing that would leave them unable to fix a broken mapping.
/// </summary>
public sealed class RequestEffectDefinitionService : IRequestEffectDefinitionService
{
    private readonly ApplicationDbContext _db;
    private readonly IEffectConfigurationValidator _validator;
    private readonly IEffectActionCatalog _catalog;
    private readonly IEffectExecutorRegistry _executors;
    private readonly IEffectPermissionGuard _permissions;

    public RequestEffectDefinitionService(
        ApplicationDbContext db,
        IEffectConfigurationValidator validator,
        IEffectActionCatalog catalog,
        IEffectExecutorRegistry executors,
        IEffectPermissionGuard permissions)
    { _db = db; _validator = validator; _catalog = catalog; _executors = executors; _permissions = permissions; }

    public async Task<IReadOnlyList<RequestEffectDefinitionDto>> ListAsync(Guid requestTypeId, CancellationToken ct)
    {
        await LoadTypeAsync(requestTypeId, ct);
        var effects = await _db.RequestEffectDefinitions.AsNoTracking()
            .Where(e => e.RequestTypeId == requestTypeId)
            .OrderBy(e => e.Sequence).ThenBy(e => e.Id)
            .ToListAsync(ct);
        return effects.Select(e => EffectMapper.ToDto(e, _catalog, _executors)).ToList();
    }

    public async Task<RequestEffectDefinitionDto> AddAsync(Guid requestTypeId, UpsertEffectDefinitionInput input, CancellationToken ct)
    {
        var type = await LoadTypeAsync(requestTypeId, ct);
        _permissions.EnsureCanConfigure(input.EffectType);
        await EnsureConfigurationValidAsync(type.FormDefinitionId, input, ct);

        var nextSequence = input.Sequence > 0
            ? input.Sequence
            : (await _db.RequestEffectDefinitions.Where(e => e.RequestTypeId == requestTypeId)
                .Select(e => (int?)e.Sequence).MaxAsync(ct) ?? 0) + 1;

        var effect = new RequestEffectDefinition
        {
            RequestTypeId = requestTypeId,
            EffectType = input.EffectType,
            Trigger = input.Trigger,
            EffectVersion = input.EffectVersion,
            Sequence = nextSequence,
            IsEnabled = input.IsEnabled,
            // Never from client input. Required-ness is declared by SystemRequestEffects and applied
            // by provisioning; a caller marking their own effect required would make it undeletable
            // with nothing able to repair or revoke it.
            IsRequired = false,
            ExecutionMode = DescriptorFor(input.EffectType).ExecutionMode,
            ConfigurationJson = input.ConfigurationJson,
        };
        _db.RequestEffectDefinitions.Add(effect);
        await _db.SaveChangesAsync(ct);
        return EffectMapper.ToDto(effect, _catalog, _executors);
    }

    public async Task<RequestEffectDefinitionDto> UpdateAsync(Guid effectId, UpsertEffectDefinitionInput input, CancellationToken ct)
    {
        var effect = await LoadEffectAsync(effectId, ct);
        var type = await LoadTypeAsync(effect.RequestTypeId, ct);

        // Both sides: the caller must be allowed to touch what is there, and to install what they
        // are replacing it with. Checking only the target would let someone without Loans.Create
        // edit an existing Loan.Create effect's configuration.
        _permissions.EnsureCanConfigure(effect.EffectType);
        _permissions.EnsureCanConfigure(input.EffectType);

        // Changing which action a required effect runs is a disguised delete.
        if (effect.IsRequired && !string.Equals(effect.EffectType, input.EffectType, StringComparison.OrdinalIgnoreCase))
            throw new ForbiddenException(
                $"'{effect.EffectType}' is required by this system request and its action cannot be changed. " +
                "Add a separate effect instead.");

        if (effect.IsRequired && !input.IsEnabled)
            throw new ForbiddenException($"'{effect.EffectType}' is required and cannot be disabled.");

        await EnsureConfigurationValidAsync(type.FormDefinitionId, input, ct);

        if (!effect.IsRequired)
        {
            effect.EffectType = input.EffectType;
            effect.Trigger = input.Trigger;
            effect.ExecutionMode = DescriptorFor(input.EffectType).ExecutionMode;
            effect.IsEnabled = input.IsEnabled;
        }

        // Remapping inputs and reordering stay open even on a required effect: a renamed form field
        // has to be fixable, and order is a tenant's to choose.
        effect.EffectVersion = input.EffectVersion;
        effect.ConfigurationJson = input.ConfigurationJson;
        if (input.Sequence > 0) effect.Sequence = input.Sequence;

        await _db.SaveChangesAsync(ct);
        return EffectMapper.ToDto(effect, _catalog, _executors);
    }

    public async Task<RequestEffectDefinitionDto> SetEnabledAsync(Guid effectId, bool enabled, CancellationToken ct)
    {
        var effect = await LoadEffectAsync(effectId, ct);
        _permissions.EnsureCanConfigure(effect.EffectType);
        if (effect.IsRequired && !enabled)
            throw new ForbiddenException($"'{effect.EffectType}' is required by this system request and cannot be disabled.");

        effect.IsEnabled = enabled;
        await _db.SaveChangesAsync(ct);
        return EffectMapper.ToDto(effect, _catalog, _executors);
    }

    public async Task DeleteAsync(Guid effectId, CancellationToken ct)
    {
        var effect = await LoadEffectAsync(effectId, ct);
        _permissions.EnsureCanConfigure(effect.EffectType);
        if (effect.IsRequired)
            throw new ForbiddenException(
                $"'{effect.EffectType}' is required by this system request and cannot be deleted. " +
                "Duplicate the request type if you need a version without it.");

        _db.RequestEffectDefinitions.Remove(effect);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<RequestEffectDefinitionDto>> ReorderAsync(
        Guid requestTypeId, ReorderEffectsInput input, CancellationToken ct)
    {
        await LoadTypeAsync(requestTypeId, ct);
        var effects = await _db.RequestEffectDefinitions
            .Where(e => e.RequestTypeId == requestTypeId).ToListAsync(ct);

        var byId = effects.ToDictionary(e => e.Id);
        // Every id must belong to this request type: accepting a foreign id would let a caller
        // discover, or renumber, an effect on somebody else's request type.
        var unknown = input.EffectIds.Where(id => !byId.ContainsKey(id)).ToList();
        if (unknown.Count > 0)
            throw new ValidationException(new[]
            {
                new ValidationFailure("effectIds", $"{unknown.Count} effect id(s) do not belong to this request type."),
            });

        var seq = 0;
        foreach (var id in input.EffectIds) byId[id].Sequence = ++seq;
        // Anything the caller omitted keeps a stable relative order after the listed ones rather
        // than silently colliding on sequence 0.
        foreach (var leftover in effects.Where(e => !input.EffectIds.Contains(e.Id)).OrderBy(e => e.Sequence))
            leftover.Sequence = ++seq;

        await _db.SaveChangesAsync(ct);
        return await ListAsync(requestTypeId, ct);
    }

    public async Task<ValidationResultDto> ValidateAsync(Guid requestTypeId, UpsertEffectDefinitionInput input, CancellationToken ct)
    {
        var type = await LoadTypeAsync(requestTypeId, ct);
        _permissions.EnsureCanConfigure(input.EffectType);
        var result = await _validator.ValidateEffectAsync(
            input.EffectType, input.Trigger, input.ConfigurationJson, type.FormDefinitionId, ct);
        return new ValidationResultDto
        {
            IsValid = result.IsValid,
            Errors = result.Errors.Select(e => new ValidationErrorDto
            {
                EffectType = e.EffectType, Field = e.Field, MessageAr = e.MessageAr, MessageEn = e.MessageEn,
            }).ToList(),
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private EffectActionDescriptor DescriptorFor(string effectType)
        => _catalog.Find(effectType)
           ?? throw new ValidationException(new[]
           {
               new ValidationFailure("effectType", $"Unknown effect action: {effectType}"),
           });

    /// <summary>Refuses to store a configuration that would fail at approval time.</summary>
    private async Task EnsureConfigurationValidAsync(Guid formDefinitionId, UpsertEffectDefinitionInput input, CancellationToken ct)
    {
        DescriptorFor(input.EffectType);   // unknown action → 400 before anything is written

        var result = await _validator.ValidateEffectAsync(
            input.EffectType, input.Trigger, input.ConfigurationJson, formDefinitionId, ct);
        if (!result.IsValid)
            throw new ValidationException(result.Errors
                .Select(e => new ValidationFailure(e.Field, e.MessageEn)).ToList());
    }

    private async Task<RequestType> LoadTypeAsync(Guid id, CancellationToken ct)
        => await _db.RequestTypes.FirstOrDefaultAsync(t => t.Id == id, ct)
           ?? throw new NotFoundException("RequestType", id);

    private async Task<RequestEffectDefinition> LoadEffectAsync(Guid id, CancellationToken ct)
        => await _db.RequestEffectDefinitions.FirstOrDefaultAsync(e => e.Id == id, ct)
           ?? throw new NotFoundException("RequestEffectDefinition", id);
}
