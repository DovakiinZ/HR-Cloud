using HR.Domain.Common;

namespace HR.Domain.Engines.Attendance;

/// <summary>An approved attendance permission (استئذان): the employee is excused for a time window on
/// one day, so the late/shortage minutes overlapping the window are waived by the calculation engine.
/// Rows are immutable audit records; one is written per approved ATTENDANCE_PERMISSION request.</summary>
public class AttendancePermission : TenantEntity
{
    public Guid EmployeeId { get; set; }

    /// <summary>The working day the permission applies to (naïve local date; TODO(tz)).</summary>
    public DateTime Date { get; set; }

    /// <summary>Permitted window as minutes-from-midnight on <see cref="Date"/>.</summary>
    public int FromMinutes { get; set; }
    public int ToMinutes { get; set; }

    /// <summary>Snapshot of window∩shift minutes at approval — the value tallied against the monthly cap.</summary>
    public int ExcusedMinutes { get; set; }

    public string? Reason { get; set; }

    /// <summary>The request instance that produced this row (idempotency + audit link).</summary>
    public Guid RequestInstanceId { get; set; }

    /// <summary>The permission type (AttendancePermissionType MasterDataItem) this row belongs to.
    /// Nullable: legacy rows and rows created before the executor stamps the id remain null.
    /// Do NOT backfill. Per-type usage queries filter on this column; null rows match no specific type.</summary>
    public Guid? PermissionTypeId { get; set; }

    /// <summary>Always <see cref="AttendanceSources.AttendancePermission"/>.</summary>
    public string? Source { get; set; }

    public Guid? CreatedByUserId { get; set; }
}
