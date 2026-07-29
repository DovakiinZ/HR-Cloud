using HR.Domain.Common;
using HR.Domain.Enums;

namespace HR.Domain.Engines.Attendance;

/// <summary>Tenant-level attendance defaults applied when a shift does not override them (e.g. default
/// grace, rounding, and whether absence is auto-marked for days with no punches).</summary>
public class AttendancePolicy : TenantEntity
{
    public string NameAr { get; set; } = "السياسة الافتراضية";
    public string NameEn { get; set; } = "Default Policy";

    /// <summary>Default grace (minutes) applied when a shift has no grace-after-start configured.</summary>
    public int DefaultGraceMinutes { get; set; } = 0;

    /// <summary>Round worked minutes to the nearest N minutes (0 = no rounding).</summary>
    public int RoundingMinutes { get; set; } = 0;

    /// <summary>Mark a working day with no punches as Absent (vs. leaving it blank).</summary>
    public bool AutoMarkAbsent { get; set; } = true;

    /// <summary>Count overtime only when the shift allows it.</summary>
    public bool CountOvertime { get; set; } = true;

    // ── Attendance-permission (استئذان) monthly cap. Null = unlimited. ──
    /// <summary>Max approved permissions per employee per calendar month (null = unlimited).</summary>
    public int? PermissionMaxPerMonth { get; set; }
    /// <summary>Max excused permission minutes per employee per calendar month (null = unlimited).</summary>
    public int? PermissionMaxMinutesPerMonth { get; set; }
    /// <summary>Block rejects an over-cap permission at approval; Warn only flags it.</summary>
    public PermissionCapMode PermissionCapMode { get; set; } = PermissionCapMode.Warn;

    public bool IsDefault { get; set; } = true;
    public bool IsActive { get; set; } = true;
}
