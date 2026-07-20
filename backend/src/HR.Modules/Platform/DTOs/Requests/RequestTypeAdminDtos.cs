using HR.Domain.Enums;

namespace HR.Modules.Platform.DTOs.Requests;

/// <summary>Summary row for the request-type list.</summary>
public record RequestTypeListItemDto
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameAr { get; init; }
    public required string NameEn { get; init; }
    public string? DescriptionAr { get; init; }
    public string? DescriptionEn { get; init; }
    public Guid? CategoryId { get; init; }
    public string? Icon { get; init; }
    public string? Color { get; init; }
    public required bool IsSystem { get; init; }
    public required bool IsActive { get; init; }
    public required int SeedVersion { get; init; }
    public required int SortOrder { get; init; }
    public required bool HasForm { get; init; }
    public required bool HasWorkflow { get; init; }
    public required int EffectCount { get; init; }
    /// <summary>False for system requests — the API refuses to delete them.</summary>
    public required bool CanDelete { get; init; }
}

public sealed record RequestTypeDetailAdminDto : RequestTypeListItemDto
{
    public required Guid FormDefinitionId { get; init; }
    public Guid? WorkflowDefinitionId { get; init; }
    public Guid? PrintTemplateId { get; init; }
    public Guid? LeaveTypeId { get; init; }
    public required IReadOnlyList<RequestEffectDefinitionDto> Effects { get; init; }
}

public sealed record RequestEffectDefinitionDto
{
    public required Guid Id { get; init; }
    public required Guid RequestTypeId { get; init; }
    public required string EffectType { get; init; }
    public required string Trigger { get; init; }
    public required int EffectVersion { get; init; }
    public required int Sequence { get; init; }
    public required bool IsEnabled { get; init; }
    public required bool IsRequired { get; init; }
    public required string ExecutionMode { get; init; }
    public required string ConfigurationJson { get; init; }
    /// <summary>False when IsRequired — required effects cannot be deleted or disabled.</summary>
    public required bool CanDelete { get; init; }
    public required bool CanDisable { get; init; }
    /// <summary>The catalog label, when the action is still registered. Null for a retired action,
    /// which is how the UI shows an effect that needs attention.</summary>
    public string? LabelAr { get; init; }
    public string? LabelEn { get; init; }
    public required bool ExecutorAvailable { get; init; }
}

// ── Write models ──────────────────────────────────────────────────────────────

public sealed record CreateRequestTypeInput
{
    public required string Code { get; init; }
    public required string NameAr { get; init; }
    public required string NameEn { get; init; }
    public string? DescriptionAr { get; init; }
    public string? DescriptionEn { get; init; }
    public Guid? CategoryId { get; init; }
    public required Guid FormDefinitionId { get; init; }
    public Guid? WorkflowDefinitionId { get; init; }
    public string? Icon { get; init; }
    public string? Color { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>
/// The fields a tenant may change on an existing request type.
///
/// Deliberately narrower than create: Code is absent because it is the stable identity that
/// provisioning, seeding and effect declarations all key on, and IsSystem is absent because nothing
/// a client sends may promote or demote a request type.
/// </summary>
public sealed record UpdateRequestTypeInput
{
    public required string NameAr { get; init; }
    public required string NameEn { get; init; }
    public string? DescriptionAr { get; init; }
    public string? DescriptionEn { get; init; }
    public Guid? CategoryId { get; init; }
    public Guid? WorkflowDefinitionId { get; init; }
    public Guid? PrintTemplateId { get; init; }
    public string? Icon { get; init; }
    public string? Color { get; init; }
    public int SortOrder { get; init; }
}

public sealed record DuplicateRequestTypeInput
{
    /// <summary>Optional. A unique code is generated from the source when omitted.</summary>
    public string? Code { get; init; }
    public required string NameAr { get; init; }
    public required string NameEn { get; init; }
}

public sealed record UpsertEffectDefinitionInput
{
    public required string EffectType { get; init; }
    public EffectTrigger Trigger { get; init; } = EffectTrigger.FinalApproval;
    public int EffectVersion { get; init; } = 1;
    public int Sequence { get; init; }
    public bool IsEnabled { get; init; } = true;
    /// <summary>Input key → {source, key}, serialized. Validated against the action descriptor and
    /// the linked form before it is stored.</summary>
    public string ConfigurationJson { get; init; } = "{}";
}

public sealed record ReorderEffectsInput
{
    /// <summary>Effect ids in the order they should execute.</summary>
    public required IReadOnlyList<Guid> EffectIds { get; init; }
}

// ── Catalog + validation projections ──────────────────────────────────────────

public sealed record EffectActionDto
{
    public required string EffectType { get; init; }
    public required string LabelAr { get; init; }
    public required string LabelEn { get; init; }
    public required string DescriptionAr { get; init; }
    public required string DescriptionEn { get; init; }
    public required string Module { get; init; }
    public required IReadOnlyList<string> SupportedTriggers { get; init; }
    public required string ExecutionMode { get; init; }
    public required IReadOnlyList<EffectInputDto> Inputs { get; init; }
    public required IReadOnlyList<string> RequiredPermissions { get; init; }
    /// <summary>Whether an executor is actually registered. A descriptor without one must not be
    /// offered for configuration — it would fail at approval time.</summary>
    public required bool ExecutorAvailable { get; init; }
}

public sealed record EffectInputDto
{
    public required string Key { get; init; }
    public required string LabelAr { get; init; }
    public required string LabelEn { get; init; }
    public required bool IsRequired { get; init; }
    public required IReadOnlyList<string> AllowedSources { get; init; }
}

public sealed record ValidationErrorDto
{
    public string? EffectType { get; init; }
    public required string Field { get; init; }
    public required string MessageAr { get; init; }
    public required string MessageEn { get; init; }
}

public sealed record ValidationResultDto
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<ValidationErrorDto> Errors { get; init; }
}

// ── Asset lookup ──────────────────────────────────────────────────────────────

public sealed record AssignableAssetDto
{
    public required Guid Id { get; init; }
    public required string Code { get; init; }
    public required string NameAr { get; init; }
    public required string NameEn { get; init; }
    public Guid? CategoryId { get; init; }
    public string? CategoryNameAr { get; init; }
    public string? CategoryNameEn { get; init; }
    /// <summary>"Available" or "Assigned". Assigned assets are excluded from the assignable feed,
    /// but the field exists so the same shape can back an admin list that shows both.</summary>
    public required string Status { get; init; }
}
