using FluentAssertions;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Reports;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ReportAccessResolverTests
{
    private static ReportDefinition Report(Guid owner, ReportScope scope) =>
        new() { Id = Guid.NewGuid(), OwnerId = owner, Scope = scope };

    private static ReportAccessContext Ctx(Guid user, Guid? dept = null, params Guid[] roles) =>
        new() { UserId = user, DepartmentId = dept, RoleIds = new HashSet<Guid>(roles) };

    [Fact]
    public void Owner_can_read_and_edit_private_report()
    {
        var me = Guid.NewGuid();
        var r = Report(me, ReportScope.Personal);
        ReportAccessResolver.CanRead(r, Array.Empty<ReportShare>(), Ctx(me)).Should().BeTrue();
        ReportAccessResolver.CanEdit(r, Array.Empty<ReportShare>(), Ctx(me)).Should().BeTrue();
    }

    [Fact]
    public void Stranger_cannot_read_private_report()
    {
        var r = Report(Guid.NewGuid(), ReportScope.Personal);
        ReportAccessResolver.CanRead(r, Array.Empty<ReportShare>(), Ctx(Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public void Anyone_can_read_company_public_report_but_not_edit()
    {
        var stranger = Guid.NewGuid();
        var r = Report(Guid.NewGuid(), ReportScope.Company);
        ReportAccessResolver.CanRead(r, Array.Empty<ReportShare>(), Ctx(stranger)).Should().BeTrue();
        ReportAccessResolver.CanEdit(r, Array.Empty<ReportShare>(), Ctx(stranger)).Should().BeFalse();
    }

    [Fact]
    public void User_share_grants_read_and_edit_respects_CanEdit()
    {
        var me = Guid.NewGuid();
        var r = Report(Guid.NewGuid(), ReportScope.Personal);
        var shares = new[] { new ReportShare { ReportDefinitionId = r.Id, SharedWithUserId = me, CanEdit = true } };
        ReportAccessResolver.CanRead(r, shares, Ctx(me)).Should().BeTrue();
        ReportAccessResolver.CanEdit(r, shares, Ctx(me)).Should().BeTrue();

        var readonlyShare = new[] { new ReportShare { ReportDefinitionId = r.Id, SharedWithUserId = me, CanEdit = false } };
        ReportAccessResolver.CanEdit(r, readonlyShare, Ctx(me)).Should().BeFalse();
    }

    [Fact]
    public void Role_share_grants_read()
    {
        var role = Guid.NewGuid();
        var r = Report(Guid.NewGuid(), ReportScope.Personal);
        var shares = new[] { new ReportShare { ReportDefinitionId = r.Id, SharedWithRoleId = role } };
        ReportAccessResolver.CanRead(r, shares, Ctx(Guid.NewGuid(), null, role)).Should().BeTrue();
    }

    [Fact]
    public void Department_share_grants_read()
    {
        var dept = Guid.NewGuid();
        var r = Report(Guid.NewGuid(), ReportScope.Personal);
        var shares = new[] { new ReportShare { ReportDefinitionId = r.Id, SharedWithDepartmentId = dept } };
        ReportAccessResolver.CanRead(r, shares, Ctx(Guid.NewGuid(), dept)).Should().BeTrue();
    }
}
