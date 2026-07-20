namespace HR.Modules.Platform.Services.Reports;

/// <summary>
/// What makes a report system-managed, in one place.
///
/// System reports are regenerated wholesale by SeedSystemReports — re-running the seed hard-removes
/// every SYS_* report and rebuilds it from the field registry. Anything a user edited into one is
/// destroyed on the next seed with no warning, so they are deliberately not editable: the supported
/// path is to clone one and customise the copy, which is what their own description tells the user
/// to do.
///
/// Identified by code prefix rather than a flag because that is already how the codebase recognises
/// them (SeedSystemReportsCommandHandler selects `Code.StartsWith("SYS_")` to regenerate), and it
/// needs no schema change.
/// </summary>
public static class ReportSystemPolicy
{
    public const string SystemCodePrefix = "SYS_";

    public static bool IsSystemManaged(string? code)
        => code is not null && code.StartsWith(SystemCodePrefix, StringComparison.OrdinalIgnoreCase);
}
