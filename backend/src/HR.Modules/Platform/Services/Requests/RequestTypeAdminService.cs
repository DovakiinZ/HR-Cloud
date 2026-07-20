using HR.Application.Common.Exceptions;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.DTOs.Requests;
using HR.Modules.Platform.Services.Completion;
using Microsoft.EntityFrameworkCore;
using FluentValidation.Results;

namespace HR.Modules.Platform.Services.Requests;

public interface IRequestTypeAdminService
{
    Task<IReadOnlyList<RequestTypeListItemDto>> ListAsync(bool includeInactive, CancellationToken ct);
    Task<RequestTypeDetailAdminDto> GetAsync(Guid id, CancellationToken ct);
    Task<RequestTypeDetailAdminDto> CreateAsync(CreateRequestTypeInput input, CancellationToken ct);
    Task<RequestTypeDetailAdminDto> UpdateAsync(Guid id, UpdateRequestTypeInput input, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<RequestTypeDetailAdminDto> DuplicateAsync(Guid id, DuplicateRequestTypeInput input, CancellationToken ct);
    Task<ValidationResultDto> ValidateAsync(Guid id, CancellationToken ct);
    Task<RequestTypeDetailAdminDto> SetActiveAsync(Guid id, bool active, CancellationToken ct);
}

/// <summary>
/// All request-type business rules live here, not in the controller. The protections are the point:
///
///   • A system request cannot be deleted, and cannot be demoted to a custom one.
///   • Code is immutable after creation — provisioning, seeding and SystemRequestEffects all key on it.
///   • IsSystem is never taken from client input, on create or update.
///   • Activation is refused unless the whole configuration would actually execute.
///
/// Tenant isolation comes from the global query filter rather than explicit predicates: every read
/// here is already scoped, and a cross-tenant id simply does not resolve, surfacing as 404 rather
/// than leaking that the row exists elsewhere.
/// </summary>
public sealed class RequestTypeAdminService : IRequestTypeAdminService
{
    private readonly ApplicationDbContext _db;
    private readonly IEffectConfigurationValidator _validator;
    private readonly IEffectActionCatalog _catalog;
    private readonly IEffectExecutorRegistry _executors;
    private readonly IEffectPermissionGuard _permissions;

    public RequestTypeAdminService(
        ApplicationDbContext db,
        IEffectConfigurationValidator validator,
        IEffectActionCatalog catalog,
        IEffectExecutorRegistry executors,
        IEffectPermissionGuard permissions)
    { _db = db; _validator = validator; _catalog = catalog; _executors = executors; _permissions = permissions; }

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<RequestTypeListItemDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var q = _db.RequestTypes.AsNoTracking();
        if (!includeInactive) q = q.Where(t => t.IsActive);

        var rows = await q
            .OrderBy(t => t.SortOrder).ThenBy(t => t.NameAr)
            .Select(t => new
            {
                Type = t,
                EffectCount = _db.RequestEffectDefinitions.Count(e => e.RequestTypeId == t.Id),
            })
            .ToListAsync(ct);

        return rows.Select(r => ToListItem(r.Type, r.EffectCount)).ToList();
    }

    public async Task<RequestTypeDetailAdminDto> GetAsync(Guid id, CancellationToken ct)
        => ToDetail(await LoadAsync(id, ct));

    // ── Create / update ───────────────────────────────────────────────────────

    public async Task<RequestTypeDetailAdminDto> CreateAsync(CreateRequestTypeInput input, CancellationToken ct)
    {
        var code = NormalizeCode(input.Code);
        if (string.IsNullOrWhiteSpace(code)) throw Invalid("code", "الكود مطلوب.", "Code is required.");
        if (await _db.RequestTypes.AnyAsync(t => t.Code == code, ct))
            throw new ConflictException($"A request type with code '{code}' already exists.");

        await EnsureFormExistsAsync(input.FormDefinitionId, ct);
        if (input.WorkflowDefinitionId is { } wf) await EnsureWorkflowExistsAsync(wf, ct);

        var type = new RequestType
        {
            Code = code,
            NameAr = input.NameAr, NameEn = input.NameEn,
            DescriptionAr = input.DescriptionAr, DescriptionEn = input.DescriptionEn,
            CategoryId = input.CategoryId,
            FormDefinitionId = input.FormDefinitionId,
            WorkflowDefinitionId = input.WorkflowDefinitionId,
            Icon = input.Icon, Color = input.Color,
            SortOrder = input.SortOrder,
            Kind = RequestKind.Dynamic,
            // Never from input. A client-created request type is the tenant's, not the product's.
            IsSystem = false,
            SeedVersion = 0,
            // Inactive until it validates — activation is the gate, not creation.
            IsActive = false,
        };
        _db.RequestTypes.Add(type);
        await _db.SaveChangesAsync(ct);
        return ToDetail(await LoadAsync(type.Id, ct));
    }

    public async Task<RequestTypeDetailAdminDto> UpdateAsync(Guid id, UpdateRequestTypeInput input, CancellationToken ct)
    {
        var type = await LoadAsync(id, ct);

        // Labels, presentation, category, workflow and print template are all a tenant's to change,
        // on a system request as much as on their own. Code and IsSystem are not on the input at all.
        type.NameAr = input.NameAr;
        type.NameEn = input.NameEn;
        type.DescriptionAr = input.DescriptionAr;
        type.DescriptionEn = input.DescriptionEn;
        type.CategoryId = input.CategoryId;
        type.Icon = input.Icon;
        type.Color = input.Color;
        type.SortOrder = input.SortOrder;

        if (input.WorkflowDefinitionId is { } wf)
        {
            await EnsureWorkflowExistsAsync(wf, ct);
            type.WorkflowDefinitionId = wf;
        }
        if (input.PrintTemplateId is { } pt) type.PrintTemplateId = pt;

        await _db.SaveChangesAsync(ct);
        return ToDetail(await LoadAsync(id, ct));
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var type = await LoadAsync(id, ct);

        // The protection the UI must not be the only enforcer of. A system request is part of the
        // product: provisioning would recreate it on the next pass, and instances referencing it
        // would be orphaned in between.
        if (type.IsSystem)
            throw new ForbiddenException(
                "System request types cannot be deleted. Deactivate it, or duplicate it and customise the copy.");

        if (await _db.RequestInstances.AnyAsync(i => i.RequestTypeId == id, ct))
            throw new ConflictException(
                "This request type has submitted requests and cannot be deleted. Deactivate it instead.");

        type.IsDeleted = true;
        type.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ── Duplicate ─────────────────────────────────────────────────────────────

    public async Task<RequestTypeDetailAdminDto> DuplicateAsync(Guid id, DuplicateRequestTypeInput input, CancellationToken ct)
    {
        var source = await LoadAsync(id, ct);

        // Duplication installs a copy of every effect, so it needs the same authority as attaching
        // them one by one. Otherwise copying a system request would be a way to acquire actions the
        // caller cannot configure.
        foreach (var effect in source.Effects)
            _permissions.EnsureCanConfigure(effect.EffectType);

        var code = string.IsNullOrWhiteSpace(input.Code)
            ? await GenerateUniqueCodeAsync(source.Code, ct)
            : NormalizeCode(input.Code!);

        if (await _db.RequestTypes.AnyAsync(t => t.Code == code, ct))
            throw new ConflictException($"A request type with code '{code}' already exists.");

        var copy = new RequestType
        {
            Code = code,
            NameAr = input.NameAr, NameEn = input.NameEn,
            DescriptionAr = source.DescriptionAr, DescriptionEn = source.DescriptionEn,
            CategoryId = source.CategoryId,
            // Form and workflow are referenced, not cloned: they are independently versioned and
            // shared, and copying them would fork definitions that the source still owns.
            FormDefinitionId = source.FormDefinitionId,
            WorkflowDefinitionId = source.WorkflowDefinitionId,
            PrintTemplateId = source.PrintTemplateId,
            LeaveTypeId = source.LeaveTypeId,
            Icon = source.Icon, Color = source.Color,
            SortOrder = source.SortOrder,
            Kind = RequestKind.Dynamic,
            // A copy of a system request is always the tenant's own. It is not provisioned, not
            // upgraded, and freely deletable — which is exactly what makes duplication the supported
            // way to customise a system request beyond its labels.
            IsSystem = false,
            SeedVersion = 0,
            IsActive = false,
        };
        _db.RequestTypes.Add(copy);

        foreach (var effect in source.Effects.OrderBy(e => e.Sequence).ThenBy(e => e.Id))
        {
            _db.RequestEffectDefinitions.Add(new RequestEffectDefinition
            {
                RequestTypeId = copy.Id,
                EffectType = effect.EffectType,
                Trigger = effect.Trigger,
                EffectVersion = effect.EffectVersion,
                Sequence = effect.Sequence,
                IsEnabled = effect.IsEnabled,
                // Required-ness belongs to the system request, not to a copy of it. Carrying it over
                // would leave the tenant unable to remove an effect from a request that is entirely
                // theirs, and nothing would ever repair it since provisioning ignores custom types.
                IsRequired = false,
                ExecutionMode = effect.ExecutionMode,
                ConfigurationJson = effect.ConfigurationJson,
            });
        }

        await _db.SaveChangesAsync(ct);
        return ToDetail(await LoadAsync(copy.Id, ct));
    }

    // ── Validation / activation ───────────────────────────────────────────────

    public async Task<ValidationResultDto> ValidateAsync(Guid id, CancellationToken ct)
    {
        await LoadAsync(id, ct);   // 404s a cross-tenant or unknown id before validating
        var result = await _validator.ValidateRequestTypeAsync(id, ct);
        return ToDto(result);
    }

    public async Task<RequestTypeDetailAdminDto> SetActiveAsync(Guid id, bool active, CancellationToken ct)
    {
        var type = await LoadAsync(id, ct);

        if (active)
        {
            // Publishing is when the configuration becomes real, so the publisher must be allowed to
            // configure every action they are switching on — otherwise a user could assemble a
            // request with Loan.Create while unable to attach it, and activate it anyway.
            foreach (var effect in type.Effects.Where(e => e.IsEnabled))
                _permissions.EnsureCanConfigure(effect.EffectType);

            var result = await _validator.ValidateRequestTypeAsync(id, ct);
            if (!result.IsValid)
                throw new ValidationException(result.Errors
                    .Select(e => new ValidationFailure(e.EffectType is null ? e.Field : $"{e.EffectType}.{e.Field}", e.MessageEn))
                    .ToList());

            // Validation covers what a caller configured. This covers what the product requires of a
            // system request: a required effect that has gone missing entirely would pass every
            // per-effect rule, because there is nothing there to fail.
            EnsureRequiredEffectsIntact(type);
        }

        type.IsActive = active;
        await _db.SaveChangesAsync(ct);
        return ToDetail(await LoadAsync(id, ct));
    }

    private static void EnsureRequiredEffectsIntact(RequestType type)
    {
        if (!SystemRequestEffects.Required.TryGetValue(type.Code, out var required)) return;

        var failures = new List<ValidationFailure>();
        foreach (var spec in required)
        {
            var match = type.Effects.FirstOrDefault(e =>
                string.Equals(e.EffectType, spec.EffectType, StringComparison.OrdinalIgnoreCase)
                && e.Trigger == spec.Trigger);

            if (match is null)
                failures.Add(new ValidationFailure(spec.EffectType,
                    $"Required effect '{spec.EffectType}' is missing. Re-run provisioning to restore it."));
            else if (!match.IsEnabled)
                failures.Add(new ValidationFailure(spec.EffectType,
                    $"Required effect '{spec.EffectType}' is disabled and must be enabled."));
        }

        if (failures.Count > 0) throw new ValidationException(failures);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<RequestType> LoadAsync(Guid id, CancellationToken ct)
        => await _db.RequestTypes.Include(t => t.Effects).FirstOrDefaultAsync(t => t.Id == id, ct)
           ?? throw new NotFoundException("RequestType", id);

    private async Task EnsureFormExistsAsync(Guid formId, CancellationToken ct)
    {
        if (formId == Guid.Empty || !await _db.FormDefinitions.AnyAsync(f => f.Id == formId, ct))
            throw Invalid("formDefinitionId", "النموذج غير موجود.", "Form definition not found.");
    }

    private async Task EnsureWorkflowExistsAsync(Guid workflowId, CancellationToken ct)
    {
        if (!await _db.WorkflowDefinitions.AnyAsync(w => w.Id == workflowId, ct))
            throw Invalid("workflowDefinitionId", "دورة الاعتماد غير موجودة.", "Workflow definition not found.");
    }

    /// <summary>"LEAVE_REQUEST" → "LEAVE_REQUEST_COPY", then _COPY2, _COPY3… until free.</summary>
    private async Task<string> GenerateUniqueCodeAsync(string sourceCode, CancellationToken ct)
    {
        var baseCode = $"{sourceCode}_COPY";
        if (!await _db.RequestTypes.AnyAsync(t => t.Code == baseCode, ct)) return baseCode;

        for (var n = 2; n < 1000; n++)
        {
            var candidate = $"{baseCode}{n}";
            if (!await _db.RequestTypes.AnyAsync(t => t.Code == candidate, ct)) return candidate;
        }
        // Falls back to something guaranteed free rather than failing the copy outright.
        return $"{sourceCode}_{Guid.NewGuid():N}"[..Math.Min(100, sourceCode.Length + 33)].ToUpperInvariant();
    }

    private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private static ValidationException Invalid(string field, string ar, string en)
        => new(new[] { new ValidationFailure(field, en) });

    private static ValidationResultDto ToDto(EffectValidationResult r) => new()
    {
        IsValid = r.IsValid,
        Errors = r.Errors.Select(e => new ValidationErrorDto
        {
            EffectType = e.EffectType, Field = e.Field, MessageAr = e.MessageAr, MessageEn = e.MessageEn,
        }).ToList(),
    };

    private static RequestTypeListItemDto ToListItem(RequestType t, int effectCount) => new()
    {
        Id = t.Id, Code = t.Code, NameAr = t.NameAr, NameEn = t.NameEn,
        DescriptionAr = t.DescriptionAr, DescriptionEn = t.DescriptionEn,
        CategoryId = t.CategoryId, Icon = t.Icon, Color = t.Color,
        IsSystem = t.IsSystem, IsActive = t.IsActive, SeedVersion = t.SeedVersion, SortOrder = t.SortOrder,
        HasForm = t.FormDefinitionId != Guid.Empty,
        HasWorkflow = t.WorkflowDefinitionId is not null && t.WorkflowDefinitionId != Guid.Empty,
        EffectCount = effectCount,
        CanDelete = !t.IsSystem,
    };

    private RequestTypeDetailAdminDto ToDetail(RequestType t) => new()
    {
        Id = t.Id, Code = t.Code, NameAr = t.NameAr, NameEn = t.NameEn,
        DescriptionAr = t.DescriptionAr, DescriptionEn = t.DescriptionEn,
        CategoryId = t.CategoryId, Icon = t.Icon, Color = t.Color,
        IsSystem = t.IsSystem, IsActive = t.IsActive, SeedVersion = t.SeedVersion, SortOrder = t.SortOrder,
        HasForm = t.FormDefinitionId != Guid.Empty,
        HasWorkflow = t.WorkflowDefinitionId is not null && t.WorkflowDefinitionId != Guid.Empty,
        EffectCount = t.Effects.Count,
        CanDelete = !t.IsSystem,
        FormDefinitionId = t.FormDefinitionId,
        WorkflowDefinitionId = t.WorkflowDefinitionId,
        PrintTemplateId = t.PrintTemplateId,
        LeaveTypeId = t.LeaveTypeId,
        Effects = t.Effects.OrderBy(e => e.Sequence).ThenBy(e => e.Id)
            .Select(e => EffectMapper.ToDto(e, _catalog, _executors)).ToList(),
    };
}

/// <summary>Shared projection so the type and effect services describe an effect identically.</summary>
internal static class EffectMapper
{
    public static RequestEffectDefinitionDto ToDto(
        RequestEffectDefinition e, IEffectActionCatalog catalog, IEffectExecutorRegistry executors)
    {
        var descriptor = catalog.Find(e.EffectType);
        return new RequestEffectDefinitionDto
        {
            Id = e.Id,
            RequestTypeId = e.RequestTypeId,
            EffectType = e.EffectType,
            Trigger = e.Trigger.ToString(),
            EffectVersion = e.EffectVersion,
            Sequence = e.Sequence,
            IsEnabled = e.IsEnabled,
            IsRequired = e.IsRequired,
            ExecutionMode = e.ExecutionMode.ToString(),
            ConfigurationJson = e.ConfigurationJson,
            CanDelete = !e.IsRequired,
            CanDisable = !e.IsRequired,
            LabelAr = descriptor?.LabelAr,
            LabelEn = descriptor?.LabelEn,
            ExecutorAvailable = executors.TryResolve(e.EffectType, out _),
        };
    }
}
