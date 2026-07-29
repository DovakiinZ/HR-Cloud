namespace HR.Domain.Enums;

/// <summary>System (built-in, ships ready) vs Dynamic (tenant-created via builder).</summary>
public enum RequestKind
{
    System = 1,
    Dynamic = 2,
}

public enum RequestApprovalStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Skipped = 4,
    Returned = 5,
}

public enum EmailQueueStatus
{
    Pending = 1,
    Sent = 2,
    Failed = 3,
}

public enum RequestPermissionAction
{
    Submit = 1,
    Approve = 2,
    View = 3,
}

/// <summary>Attendance day state. Resolved by the Attendance engine (live punches + shift rules)
/// and by approved leave / missing-punch / correction requests.</summary>
public enum AttendanceStatus
{
    Present = 1,
    Absent = 2,
    OnLeave = 3,
    Holiday = 4,
    Weekend = 5,
    Late = 6,
    Remote = 7,            // legacy alias of WorkFromHome (kept for existing rows)
    MissingCheckIn = 8,
    MissingCheckOut = 9,
    ShortHours = 10,
    Overtime = 11,
    WorkFromHome = 12,
}

/// <summary>Direction of a single attendance punch.</summary>
public enum PunchDirection
{
    In = 1,
    Out = 2,
}

/// <summary>How the monthly attendance-permission cap is enforced.</summary>
public enum PermissionCapMode { Warn = 0, Block = 1 }
