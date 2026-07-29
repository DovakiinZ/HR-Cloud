namespace HR.Modules.Attendance.DTOs;

/// <summary>One computed employee/day row (the unit of the daily view + export).</summary>
public sealed class AttendanceDayDto
{
    public Guid? RecordId { get; set; }          // null for virtual (no persisted row) days
    public Guid EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string? EmployeeNumber { get; set; }
    public string? DepartmentName { get; set; }
    public string? BranchName { get; set; }
    public string? JobTitleName { get; set; }

    public DateTime Date { get; set; }

    public Guid? ShiftId { get; set; }
    public string? ShiftName { get; set; }
    public bool IsFlexible { get; set; }

    public DateTime? CheckIn { get; set; }
    public DateTime? CheckOut { get; set; }

    public int RequiredMinutes { get; set; }
    public int WorkedMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int ShortageMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
    public int BreakMinutes { get; set; }
    public int ExcusedMinutes { get; set; }

    public string Status { get; set; } = null!;
    public string? Source { get; set; }
    public Guid? ReferenceId { get; set; }
    public string? Notes { get; set; }
}

/// <summary>KPI counters for a date (top cards on the attendance page).</summary>
public sealed class AttendanceKpiDto
{
    public int Total { get; set; }
    public int Present { get; set; }
    public int Absent { get; set; }
    public int Late { get; set; }
    public int OnLeave { get; set; }
    public int MissingPunches { get; set; }
    public int ShortHours { get; set; }
    public int Overtime { get; set; }
    public int Weekend { get; set; }
    public int Holiday { get; set; }
}

public sealed class AttendanceDailyResponse
{
    public DateTime Date { get; set; }
    public AttendanceKpiDto Kpis { get; set; } = new();
    public List<AttendanceDayDto> Rows { get; set; } = new();
}

/// <summary>Per-employee aggregate over a range (weekly / monthly views).</summary>
public sealed class AttendanceSummaryDto
{
    public Guid EmployeeId { get; set; }
    public string? EmployeeName { get; set; }
    public string? EmployeeNumber { get; set; }
    public string? DepartmentName { get; set; }
    public string? BranchName { get; set; }

    public int PresentDays { get; set; }
    public int AbsentDays { get; set; }
    public int LeaveDays { get; set; }
    public int LateDays { get; set; }
    public int ShortDays { get; set; }
    public int OvertimeDays { get; set; }
    public int WeekendDays { get; set; }
    public int HolidayDays { get; set; }
    public int MissingPunchDays { get; set; }

    public int WorkedMinutes { get; set; }
    public int RequiredMinutes { get; set; }
    public int LateMinutes { get; set; }
    public int ShortageMinutes { get; set; }
    public int OvertimeMinutes { get; set; }
}

public sealed class AttendanceSummaryResponse
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public AttendanceKpiDto Kpis { get; set; } = new();
    public List<AttendanceSummaryDto> Rows { get; set; } = new();
}

// ── Detail drawer ──────────────────────────────────────────────────────────

public sealed class AttendancePunchDto
{
    public Guid Id { get; set; }
    public DateTime PunchTime { get; set; }
    public string Direction { get; set; } = null!;
    public string Source { get; set; } = null!;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Notes { get; set; }
}

public sealed class AttendanceAuditDto
{
    public string Action { get; set; } = null!;
    public string? Details { get; set; }
    public DateTime At { get; set; }
}

public sealed class AttendanceDetailDto
{
    public AttendanceDayDto Day { get; set; } = new();
    public List<AttendancePunchDto> Punches { get; set; } = new();
    public List<AttendanceAuditDto> Audit { get; set; } = new();
    public Guid? RelatedRequestId { get; set; }
    public string? RelatedRequestNumber { get; set; }
}

// ── Write payloads ─────────────────────────────────────────────────────────

public sealed class ManualPunchRequest
{
    public Guid EmployeeId { get; set; }
    public DateTime Date { get; set; }
    public string? CheckIn { get; set; }   // "HH:mm"
    public string? CheckOut { get; set; }  // "HH:mm"
    public string? Notes { get; set; }
}

public sealed class CorrectAttendanceRequest
{
    public string? CheckIn { get; set; }   // "HH:mm"
    public string? CheckOut { get; set; }  // "HH:mm"
    public string? Reason { get; set; }
}
