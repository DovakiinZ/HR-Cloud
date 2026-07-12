using HR.Domain.Engines.Reports;
using HR.Domain.Enums;

namespace HR.Modules.Platform.Services.Reports;

public sealed class ReportAccessContext
{
    public Guid UserId { get; init; }
    public Guid? DepartmentId { get; init; }
    public IReadOnlySet<Guid> RoleIds { get; init; } = new HashSet<Guid>();
}

/// <summary>Pure visibility resolution. Read = owner OR company-scope OR a matching share.
/// Edit = owner OR a share with CanEdit. No DB access here.</summary>
public static class ReportAccessResolver
{
    public static bool CanRead(ReportDefinition report, IReadOnlyList<ReportShare> shares, ReportAccessContext ctx)
    {
        if (report.OwnerId == ctx.UserId) return true;
        if (report.Scope == ReportScope.Company) return true;
        return shares.Any(s => Matches(s, ctx));
    }

    public static bool CanEdit(ReportDefinition report, IReadOnlyList<ReportShare> shares, ReportAccessContext ctx)
    {
        if (report.OwnerId == ctx.UserId) return true;
        return shares.Any(s => s.CanEdit && Matches(s, ctx));
    }

    private static bool Matches(ReportShare s, ReportAccessContext ctx)
        => (s.SharedWithUserId is { } u && u == ctx.UserId)
        || (s.SharedWithRoleId is { } r && ctx.RoleIds.Contains(r))
        || (s.SharedWithDepartmentId is { } d && ctx.DepartmentId == d);
}
