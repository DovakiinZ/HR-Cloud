using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ReportVisibilityPredicateTests
{
    private static readonly Guid Me   = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid MyRole = Guid.NewGuid();
    private static readonly Guid MyDept = Guid.NewGuid();

    private static ReportAccessContext Ctx() => new()
    {
        UserId = Me,
        DepartmentId = MyDept,
        RoleIds = new HashSet<Guid> { MyRole },
    };

    private static ReportDefinition Report(Guid owner, ReportScope scope, params ReportShare[] shares)
        => new() { Id = Guid.NewGuid(), OwnerId = owner, Scope = scope, Shares = shares.ToList() };

    [Fact]
    public void Owner_can_see_personal_report()
    {
        var reports = new[] { Report(Me, ReportScope.Personal) }.AsQueryable();
        reports.Where(ReportVisibilityPredicate.Build(Ctx())).Should().HaveCount(1);
    }

    [Fact]
    public void Non_owner_cannot_see_personal_report_without_share()
    {
        var reports = new[] { Report(Other, ReportScope.Personal) }.AsQueryable();
        reports.Where(ReportVisibilityPredicate.Build(Ctx())).Should().BeEmpty();
    }

    [Fact]
    public void Company_scope_is_visible_to_everyone()
    {
        var reports = new[] { Report(Other, ReportScope.Company) }.AsQueryable();
        reports.Where(ReportVisibilityPredicate.Build(Ctx())).Should().HaveCount(1);
    }

    [Fact]
    public void User_role_and_department_shares_grant_visibility()
    {
        var byUser = Report(Other, ReportScope.Personal, new ReportShare { SharedWithUserId = Me });
        var byRole = Report(Other, ReportScope.Personal, new ReportShare { SharedWithRoleId = MyRole });
        var byDept = Report(Other, ReportScope.Personal, new ReportShare { SharedWithDepartmentId = MyDept });
        var unrelated = Report(Other, ReportScope.Personal, new ReportShare { SharedWithUserId = Other });
        var reports = new[] { byUser, byRole, byDept, unrelated }.AsQueryable();
        reports.Where(ReportVisibilityPredicate.Build(Ctx())).Should().HaveCount(3);
    }
}
