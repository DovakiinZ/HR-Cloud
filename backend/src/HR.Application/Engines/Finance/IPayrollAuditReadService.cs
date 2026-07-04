using HR.Application.Common.Paging;

namespace HR.Application.Engines.Finance;

public sealed record PayrollAuditFilter(
    Guid? RunId = null,
    Guid? ActorUserId = null,
    string? Action = null,
    DateTime? From = null,
    DateTime? To = null);

/// <summary>One row of the payroll audit trail, projected from the existing AuditLog with the actor's
/// display name resolved.</summary>
public sealed record PayrollAuditRow(
    DateTime Timestamp,
    string Action,
    Guid? ActorUserId,
    string? ActorName,
    string EntityType,
    Guid EntityId,
    string? OldValues,
    string? NewValues);

/// <summary>A unified, filterable READ over the payroll audit trail. Reads the existing AuditLog (written
/// by every payroll engine via IAuditLogService) — no separate audit store.</summary>
public interface IPayrollAuditReadService
{
    Task<PagedResult<PayrollAuditRow>> QueryAsync(PayrollAuditFilter filter, PagedRequest request, CancellationToken ct = default);
}
