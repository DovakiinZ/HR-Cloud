namespace HR.Domain.Engines.Timeline;

/// <summary>Canonical timeline event categories for the employee journey (spec Feature 4).
/// Stored as its string name in <see cref="TimelineEvent.Category"/> (the column is a string, so this
/// enum is additive and never breaks existing rows). Later phases append Signature/Settlement/
/// Clearance/Certificate (C5) — DO NOT renumber; append only.</summary>
public enum TimelineCategory
{
    Onboarding = 0,      // candidate→employee, offer, contract, joining, probation start/end
    Assignment = 1,      // department / branch / manager / job-title changes, promotions
    Compensation = 2,    // salary & allowance changes (SENSITIVE — masked without ViewSensitive)
    Leave = 3,           // leave requests
    Attendance = 4,      // attendance corrections
    Disciplinary = 5,    // warnings & disciplinary actions
    Training = 6,        // training records
    Document = 7,        // documents added / renewed / expired
    Asset = 8,           // assets assigned / returned
    Loan = 9,            // loans & advances
    Payroll = 10,        // payroll events (run included, paid)
    AdminDecision = 11,  // administrative decisions / admin-action printing
    Offboarding = 12,    // resignation, termination, access deactivation
    // reserved for later phases (append only):
    Signature = 13,      // Phase 4 — e-signatures
    Settlement = 14,     // Phase 5 — final settlement
    Clearance = 15,      // Phase 5 — clearance
    Certificate = 16,    // Phase 5 — service certificate
}
