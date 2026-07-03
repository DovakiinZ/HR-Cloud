namespace HR.Domain.Enums;

public enum PayrollExclusionReasonCode
{
    ExcludedByScope = 1,
    NotEmployedInPeriod = 2,
    NoActiveSalary = 3,
    AlreadyInActiveRunForPeriod = 4,
}

// Origin = the UI/API surface that created the transaction (distinct from SourceModule,
// which is the business system). Values 5..10 are reserved now to avoid later redesign.
public enum PayrollTransactionOrigin
{
    System = 0,
    RunPage = 1,
    AttendanceDaily = 2,
    DeductionsPage = 3,
    AdditionsPage = 4,
    Import = 5,
    API = 6,
    Migration = 7,
    Workflow = 8,
    ESS = 9,
    Scheduler = 10,
}

public enum PayrollCalculationTriggerSource { Manual = 1, Recalculate = 2, Auto = 3 }
