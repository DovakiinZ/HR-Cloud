using System;
using System.Linq.Expressions;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>
/// EF-translatable mirror of <see cref="ReportAccessResolver.CanRead"/>:
/// Read = owner OR Company scope OR a matching share (user/role/department).
/// Kept in lockstep with the pure resolver — change both together.
/// </summary>
public static class ReportVisibilityPredicate
{
    public static Expression<Func<ReportDefinition, bool>> Build(ReportAccessContext ctx)
    {
        var uid = ctx.UserId;
        var dept = ctx.DepartmentId;
        var roleIds = ctx.RoleIds; // captured; EF translates Contains to IN (...)
        return r =>
            r.OwnerId == uid
            || r.Scope == ReportScope.Company
            || r.Shares.Any(s =>
                   (s.SharedWithUserId != null && s.SharedWithUserId == uid)
                || (s.SharedWithRoleId != null && roleIds.Contains(s.SharedWithRoleId.Value))
                || (s.SharedWithDepartmentId != null && dept != null && s.SharedWithDepartmentId == dept));
    }
}
