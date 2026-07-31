namespace HR.Domain.Engines.Attendance;

/// <summary>How a breached permission-type limit is handled.</summary>
public enum PermissionExceedBehavior { Block = 0, Warn = 1, RequireApprovalOverride = 2 }
