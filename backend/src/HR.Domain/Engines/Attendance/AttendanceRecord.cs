using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Attendance;

/// <summary>A single day's attendance state for an employee. Persisted rows are written by live
/// punches (manual / biometric / geofence / web) and by approved leave / missing-punch / correction
/// requests. The Attendance engine recomputes the calculation fields below from the day's punches and
/// the effective shift; for employees/days with no persisted row the engine synthesises a virtual
/// record on read (Weekend / Holiday / Absent).</summary>
public class AttendanceRecord : TenantEntity
{
    public Guid EmployeeId { get; set; }
    public DateTime Date { get; set; }
    public AttendanceStatus Status { get; set; }

    /// <summary>Effective shift resolved for this day (nullable for legacy / leave rows).</summary>
    public Guid? ShiftId { get; set; }

    /// <summary>Punch in/out times (first IN / last OUT of the day).</summary>
    public DateTime? CheckIn { get; set; }
    public DateTime? CheckOut { get; set; }

    // ── Calculation breakdown (minutes) — computed by AttendanceCalculationService ──
    public int RequiredMinutes { get; set; }
    public int WorkedMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int ShortageMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public int BreakMinutes { get; set; }
    public int ExcusedMinutes { get; set; }

    /// <summary>True when the resolved shift is flexible (only total hours matter).</summary>
    public bool IsFlexible { get; set; }

    /// <summary>Origin of this record. See <see cref="AttendanceSources"/>.</summary>
    public string? Source { get; set; }

    /// <summary>Links back to the request (leave / missing-punch / correction) that produced this row.</summary>
    public Guid? ReferenceId { get; set; }
    public string? Notes { get; set; }
}

/// <summary>Canonical attendance source tags (stored on <see cref="AttendanceRecord.Source"/> and
/// <see cref="AttendancePunch.Source"/>).</summary>
public static class AttendanceSources
{
    public const string Biometric = "Biometric";
    public const string MobileGeofence = "MobileGeofence";
    public const string WebCheckIn = "WebCheckIn";
    public const string ManualEntry = "ManualEntry";
    public const string ApprovedRequest = "ApprovedRequest";
    public const string LeaveRequest = "LeaveRequest";
    public const string MissingPunchRequest = "MissingPunch";
    public const string AttendanceCorrection = "AttendanceCorrection";
    public const string AttendancePermission = "AttendancePermission";
}
