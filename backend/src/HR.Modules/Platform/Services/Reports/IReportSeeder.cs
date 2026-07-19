namespace HR.Modules.Platform.Services.Reports;

/// <summary>What one seeding pass did, per report. Returned rather than logged so the caller
/// (and the API response) can tell "already there" apart from "created" apart from "skipped
/// because this tenant's model does not support it".</summary>
public sealed record ReportSeedOutcome(string Code, string NameAr, ReportSeedStatus Status, Guid? Id, string? Reason);

public enum ReportSeedStatus
{
    /// <summary>Newly written on this pass.</summary>
    Created = 1,
    /// <summary>A report with this code already existed; left completely untouched.</summary>
    AlreadyPresent = 2,
    /// <summary>The primary object or every one of its fields is absent from the live catalog.</summary>
    Unsupported = 3,
}

/// <summary>Provisions the built-in report definitions (and the ObjectDefinition rows they
/// reference) for the current tenant. Idempotent: safe to call on every start-up or by hand.</summary>
public interface IReportSeeder
{
    /// <summary>Seed every built-in report. Existing reports are never modified or deleted.</summary>
    Task<IReadOnlyList<ReportSeedOutcome>> SeedDefaultsAsync(CancellationToken ct);

    /// <summary>The built-in report codes this seeder knows how to provision.</summary>
    IReadOnlyList<string> AvailableCodes();
}
