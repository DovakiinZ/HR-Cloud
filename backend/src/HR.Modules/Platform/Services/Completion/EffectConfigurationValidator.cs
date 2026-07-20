using HR.Application.Engines.Completion;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Completion;

/// <param name="EffectType">The effect the problem belongs to, or null for a request-type-level problem.</param>
/// <param name="Field">The input key, or a pseudo-field like "form" / "workflow" / "effectType".</param>
public sealed record EffectValidationError(string? EffectType, string Field, string MessageAr, string MessageEn);

public sealed record EffectValidationResult(bool IsValid, IReadOnlyList<EffectValidationError> Errors)
{
    public static readonly EffectValidationResult Valid = new(true, Array.Empty<EffectValidationError>());
    public static EffectValidationResult Invalid(IReadOnlyList<EffectValidationError> errors) => new(false, errors);
}

public interface IEffectConfigurationValidator
{
    /// <summary>Validates every configured effect on a request type, plus the request type's own
    /// activation prerequisites (a linked form and workflow).</summary>
    Task<EffectValidationResult> ValidateRequestTypeAsync(Guid requestTypeId, CancellationToken ct);

    /// <summary>Validates a single effect configuration against a form, without persisting it —
    /// what the builder calls as the user maps inputs.</summary>
    Task<EffectValidationResult> ValidateEffectAsync(
        string effectType, EffectTrigger trigger, string? configurationJson, Guid formDefinitionId, CancellationToken ct);
}

/// <summary>
/// Enforces that a request type cannot be activated into a state that would fail at approval time.
///
/// The whole point is to move failures from "an employee's approved request silently did nothing"
/// to "you cannot publish this yet". Every rule here corresponds to a way completion would
/// otherwise break: a missing executor throws inside the completion transaction, an unmapped
/// required input reaches an executor as null, and a mapping to a form field that does not exist
/// resolves to nothing at all.
/// </summary>
public sealed class EffectConfigurationValidator : IEffectConfigurationValidator
{
    private readonly ApplicationDbContext _db;
    private readonly IEffectActionCatalog _catalog;
    private readonly IEffectExecutorRegistry _executors;

    public EffectConfigurationValidator(ApplicationDbContext db, IEffectActionCatalog catalog, IEffectExecutorRegistry executors)
    { _db = db; _catalog = catalog; _executors = executors; }

    public async Task<EffectValidationResult> ValidateRequestTypeAsync(Guid requestTypeId, CancellationToken ct)
    {
        var type = await _db.Set<RequestType>().AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == requestTypeId, ct);
        if (type is null)
            return EffectValidationResult.Invalid(new[]
            {
                new EffectValidationError(null, "requestType", "نوع الطلب غير موجود.", "Request type not found."),
            });

        var errors = new List<EffectValidationError>();

        if (type.FormDefinitionId == Guid.Empty)
            errors.Add(new EffectValidationError(null, "form", "يجب ربط نموذج بالطلب.", "A form must be linked."));

        if (type.WorkflowDefinitionId is null || type.WorkflowDefinitionId == Guid.Empty)
            errors.Add(new EffectValidationError(null, "workflow", "يجب ربط دورة اعتماد بالطلب.", "An approval workflow must be linked."));

        var fieldCodes = await FormFieldCodesAsync(type.FormDefinitionId, ct);

        var effects = await _db.Set<RequestEffectDefinition>().AsNoTracking()
            .Where(e => e.RequestTypeId == requestTypeId)
            .OrderBy(e => e.Sequence).ThenBy(e => e.Id)
            .ToListAsync(ct);

        foreach (var effect in effects)
        {
            // A disabled optional effect cannot break completion, so it does not block activation.
            // A disabled *required* effect can, and is reported below.
            if (!effect.IsEnabled && !effect.IsRequired) continue;

            if (!effect.IsEnabled && effect.IsRequired)
                errors.Add(new EffectValidationError(effect.EffectType, "isEnabled",
                    "لا يمكن تعطيل إجراء إلزامي.", "A required action cannot be disabled."));

            errors.AddRange(ValidateOne(effect.EffectType, effect.Trigger, effect.ConfigurationJson, fieldCodes));
        }

        return errors.Count == 0 ? EffectValidationResult.Valid : EffectValidationResult.Invalid(errors);
    }

    public async Task<EffectValidationResult> ValidateEffectAsync(
        string effectType, EffectTrigger trigger, string? configurationJson, Guid formDefinitionId, CancellationToken ct)
    {
        var fieldCodes = await FormFieldCodesAsync(formDefinitionId, ct);
        var errors = ValidateOne(effectType, trigger, configurationJson, fieldCodes).ToList();
        return errors.Count == 0 ? EffectValidationResult.Valid : EffectValidationResult.Invalid(errors);
    }

    private IEnumerable<EffectValidationError> ValidateOne(
        string effectType, EffectTrigger trigger, string? configurationJson, IReadOnlySet<string> fieldCodes)
    {
        // 1. The action must exist in the catalog. This is what stops an arbitrary string — a CLR
        //    type name, a table — from ever reaching the engine.
        var descriptor = _catalog.Find(effectType);
        if (descriptor is null)
        {
            yield return new EffectValidationError(effectType, "effectType",
                $"إجراء غير معروف: {effectType}", $"Unknown effect action: {effectType}");
            yield break;
        }

        // 2. A descriptor with no executor would throw inside the completion transaction and roll
        //    back an approved request. Catch it here instead.
        if (!_executors.TryResolve(effectType, out _))
            yield return new EffectValidationError(effectType, "effectType",
                "لا يوجد منفّذ مسجَّل لهذا الإجراء.", "No registered executor for this action.");

        if (!descriptor.SupportedTriggers.Contains(trigger))
            yield return new EffectValidationError(effectType, "trigger",
                "هذا الإجراء لا يدعم هذا التوقيت.", $"This action does not support the {trigger} trigger.");

        var config = EffectConfiguration.TryParse(configurationJson);
        if (config is null)
        {
            yield return new EffectValidationError(effectType, "configuration",
                "إعدادات الإجراء غير صالحة.", "The action configuration is not valid JSON.");
            yield break;
        }

        // 3. Every required input must be mapped.
        foreach (var input in descriptor.Inputs.Where(i => i.IsRequired))
        {
            if (!config.Inputs.TryGetValue(input.Key, out var mapping) || string.IsNullOrWhiteSpace(mapping.Key))
                yield return new EffectValidationError(effectType, input.Key,
                    $"الحقل المطلوب غير مرتبط: {input.LabelAr}", $"Required input not mapped: {input.LabelEn}");
        }

        // 4. Every mapping must address something that exists.
        foreach (var (key, mapping) in config.Inputs)
        {
            var input = descriptor.Input(key);
            if (input is null)
            {
                yield return new EffectValidationError(effectType, key,
                    $"مدخل غير معروف لهذا الإجراء: {key}", $"Unknown input for this action: {key}");
                continue;
            }

            if (!input.AllowedSources.Contains(mapping.Source))
            {
                yield return new EffectValidationError(effectType, key,
                    "مصدر القيمة غير مسموح لهذا المدخل.",
                    $"Source {mapping.Source} is not allowed for input '{key}'.");
                continue;
            }

            switch (mapping.Source)
            {
                case EffectValueSource.FormField when !fieldCodes.Contains(mapping.Key):
                    // The rule that catches the most real breakage: a form field renamed or removed
                    // after the effect was configured.
                    yield return new EffectValidationError(effectType, key,
                        $"حقل النموذج غير موجود: {mapping.Key}", $"Form field does not exist: {mapping.Key}");
                    break;

                case EffectValueSource.RequestContext when !RequestContextKeys.All.Contains(mapping.Key):
                    yield return new EffectValidationError(effectType, key,
                        $"مفتاح سياق غير معروف: {mapping.Key}", $"Unknown request-context key: {mapping.Key}");
                    break;

                case EffectValueSource.CurrentUser when !CurrentUserKeys.All.Contains(mapping.Key):
                    yield return new EffectValidationError(effectType, key,
                        $"مفتاح مستخدم غير معروف: {mapping.Key}", $"Unknown current-user key: {mapping.Key}");
                    break;

                case EffectValueSource.TenantContext when !TenantContextKeys.All.Contains(mapping.Key):
                    yield return new EffectValidationError(effectType, key,
                        $"مفتاح مستأجر غير معروف: {mapping.Key}", $"Unknown tenant-context key: {mapping.Key}");
                    break;

                case EffectValueSource.Constant when string.IsNullOrWhiteSpace(mapping.Key):
                    yield return new EffectValidationError(effectType, key,
                        "القيمة الثابتة فارغة.", "The constant value is empty.");
                    break;
            }
        }
    }

    private async Task<IReadOnlySet<string>> FormFieldCodesAsync(Guid formDefinitionId, CancellationToken ct)
    {
        if (formDefinitionId == Guid.Empty) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var codes = await _db.FormFields.AsNoTracking()
            .Where(f => f.FormDefinitionId == formDefinitionId)
            .Select(f => f.Code).ToListAsync(ct);
        return new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);
    }
}
