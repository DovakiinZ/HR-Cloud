using HR.Application.Engines.Completion;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.Entities;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Completion.Executors;

/// <summary>
/// Applies an approved self-service change to ONE whitelisted employee profile field
/// (contact / address / emergency contact).
///
/// Deliberately closed: <c>fieldKey</c> must be one of a fixed, safe set — an unknown key throws,
/// so an approved "employee data update" can never reach salary, bank details, GOSI, status,
/// identity or organisational placement. The previous value is returned as the effect's before-
/// state, which the completion engine persists as the audit trail.
/// </summary>
public sealed class EmployeeUpdateFieldExecutor : IEffectExecutor
{
    private readonly ApplicationDbContext _db;

    public EmployeeUpdateFieldExecutor(ApplicationDbContext db) => _db = db;

    public string EffectType => EffectTypes.EmployeeUpdateField;

    /// <summary>The ONLY fields this effect may change. Sensitive fields (salary, bank, GOSI,
    /// status, identity, department/branch/manager) are intentionally absent.</summary>
    private static readonly IReadOnlyDictionary<string, (Func<Employee, string?> Get, Action<Employee, string?> Set)> Editable =
        new Dictionary<string, (Func<Employee, string?>, Action<Employee, string?>)>(StringComparer.OrdinalIgnoreCase)
        {
            ["phone"] = (e => e.Phone, (e, v) => e.Phone = v),
            ["address"] = (e => e.Address, (e, v) => e.Address = v),
            ["city"] = (e => e.City, (e, v) => e.City = v),
            ["emergencyContactName"] = (e => e.EmergencyContactName, (e, v) => e.EmergencyContactName = v),
            ["emergencyContactPhone"] = (e => e.EmergencyContactPhone, (e, v) => e.EmergencyContactPhone = v),
        };

    /// <summary>The whitelisted field keys — exposed so validation/UI can offer exactly these.</summary>
    public static IReadOnlyCollection<string> EditableFields => (IReadOnlyCollection<string>)Editable.Keys;

    public async Task<EffectExecutionResult> ExecuteAsync(EffectContext ctx, CancellationToken ct)
    {
        var fieldKey = ctx.Str("fieldKey");
        if (string.IsNullOrWhiteSpace(fieldKey))
            throw new InvalidOperationException("Employee.UpdateField: no field was specified.");
        if (!Editable.TryGetValue(fieldKey, out var access))
            throw new InvalidOperationException(
                $"Employee.UpdateField: '{fieldKey}' is not a field that may be updated through a request.");

        // A missing value is "not provided" — skip rather than blank the field out.
        var newValue = ctx.Str("newValue");
        if (newValue is null)
            return EffectExecutionResult.Skip("NoValueProvided",
                targetEntityType: "Employee", summary: "No new value supplied; nothing changed.");

        var emp = await _db.Employees.FirstOrDefaultAsync(e => e.Id == ctx.EmployeeId, ct)
            ?? throw new InvalidOperationException($"Employee.UpdateField: employee {ctx.EmployeeId} not found.");

        var before = access.Get(emp);
        if (string.Equals(before, newValue, StringComparison.Ordinal))
            return EffectExecutionResult.Skip("NoChange",
                targetEntityType: "Employee", summary: "The value already matches; nothing changed.");

        access.Set(emp, newValue);

        return EffectExecutionResult.Ok(
            targetEntityType: "Employee",
            targetRecordId: emp.Id,
            before: new { field = fieldKey, value = before },
            after: new { field = fieldKey, value = newValue },
            summary: $"Employee field '{fieldKey}' updated");
    }
}
