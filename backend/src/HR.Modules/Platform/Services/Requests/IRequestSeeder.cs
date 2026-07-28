using HR.Domain.Enums;

namespace HR.Modules.Platform.Services.Requests;

/// <summary>
/// A shipped form-field descriptor, used by provisioning to reconcile missing system fields
/// into an existing form without re-running the full seeder.
/// </summary>
public sealed record FormFieldSpec(
    string Code,
    string NameAr,
    string NameEn,
    FieldType FieldType,
    bool IsRequired,
    string? Placeholder = null,
    string? Options = null);

public interface IRequestSeeder
{
    /// <summary>
    /// Idempotently provisions the built-in System Requests for the current tenant — each
    /// with a real Form, Workflow, Impact Mapping and (where relevant) Print Template.
    /// Guarantees "if visible, it is usable". Returns the count newly created.
    /// </summary>
    Task<int> SeedSystemRequestsAsync(CancellationToken ct);

    /// <summary>
    /// Returns the shipped <see cref="FormFieldSpec"/> list for a system request type by its code,
    /// or an empty list if the code is unknown. Used by provisioning to add fields that were absent
    /// in an older seeder version without re-running the full form creation path.
    /// </summary>
    IReadOnlyList<FormFieldSpec> SystemFormFields(string requestCode);
}
