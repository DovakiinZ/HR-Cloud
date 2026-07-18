using AutoMapper;
using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Reports.Registry;
using HR.Domain.Engines.Reports;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Commands.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

// ── Fakes ────────────────────────────────────────────────────────────────────────

file sealed class FakeUser : ICurrentUserService
{
    public Guid UserId => Guid.Parse("11111111-1111-1111-1111-111111111111");
    public Guid TenantId => Guid.Parse("22222222-2222-2222-2222-222222222222");
    public string? Email => "quick@test";
    public IReadOnlyList<string> Permissions { get; } = new[] { "Employees.View", "Payroll.View" };
    public bool IsAuthenticated => true;
}

file sealed class FakeIds : IReportObjectIdResolver
{
    private readonly Dictionary<string, Guid> _map;
    public FakeIds(params (string code, Guid id)[] e)
        => _map = e.ToDictionary(x => x.code, x => x.id, StringComparer.OrdinalIgnoreCase);
    public Guid? ResolveId(string objectCode)
        => _map.TryGetValue(objectCode, out var id) ? id : (Guid?)null;
}

/// <summary>Registry stub returning preset descriptors (mirrors the real Resolve/GetSubjects/GetFields).</summary>
file sealed class FakeRegistry : IReportFieldRegistry
{
    private readonly Dictionary<string, ReportFieldDescriptor> _byKey;
    private readonly List<ReportSubjectDescriptor> _subjects;

    public FakeRegistry(IEnumerable<ReportFieldDescriptor> fields, IEnumerable<ReportSubjectDescriptor> subjects)
    {
        _byKey = fields.ToDictionary(f => f.Key, StringComparer.OrdinalIgnoreCase);
        _subjects = subjects.ToList();
    }

    public IReadOnlyList<ReportSubjectDescriptor> GetSubjects(ReportRegistryContext ctx) => _subjects;

    public IReadOnlyList<ReportFieldDescriptor> GetFields(ReportRegistryContext ctx, string subject)
        => _byKey.Values.Where(f => string.Equals(f.Subject, subject, StringComparison.OrdinalIgnoreCase)).ToList();

    public ReportFieldDescriptor? GetField(ReportRegistryContext ctx, string key)
        => _byKey.TryGetValue(key, out var f) ? f : null;

    public ReportResolveResult Resolve(ReportRegistryContext ctx, IReadOnlyCollection<string> keys)
    {
        var matched = new List<ReportFieldDescriptor>();
        var unknown = new List<string>();
        foreach (var k in keys)
        {
            if (_byKey.TryGetValue(k, out var f)) matched.Add(f);
            else unknown.Add(k);
        }
        var joins = matched.SelectMany(f => f.JoinPath)
            .GroupBy(j => (j.SourceObjectCode, j.TargetObjectCode, j.JoinField))
            .Select(g => g.First()).ToList();
        return new ReportResolveResult(matched, joins, unknown);
    }

    public ReportRegistryHealth GetHealth()
        => new(_subjects.Count, _byKey.Count, 0, Array.Empty<ReportRegistryExclusion>());
}

/// <summary>Routes CreateQuickReportCommand straight to its handler (no MediatR pipeline in tests).</summary>
file sealed class DirectSender : ISender
{
    private readonly CreateQuickReportCommandHandler _handler;
    public DirectSender(CreateQuickReportCommandHandler handler) => _handler = handler;

    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default)
    {
        if (request is CreateQuickReportCommand c) return (TResponse)(object)await _handler.Handle(c, ct);
        throw new NotSupportedException(request.GetType().Name);
    }

    public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest
        => throw new NotSupportedException();
    public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
}

file static class Reg
{
    public static ReportJoinStep Step(string s, string t, string j) => new(s, t, j);

    public static ReportFieldDescriptor Field(
        string key, Guid objId, string objCode, string prop,
        ReportJoinStep[]? join = null, string subject = "employees", bool isDefault = true)
        => new(key, "ع", "en", subject, "grp", "Text", objId, objCode, prop,
            join ?? Array.Empty<ReportJoinStep>(), new[] { "Equals" },
            true, true, true, false, null, isDefault, 0, null, "Employees.View");

    public static ReportSubjectDescriptor Subject(string key)
        => new(key, "الموظفون", "Employees", "icon", 1);
}

// ── Tests ───────────────────────────────────────────────────────────────────────

public class QuickReportCommandTests
{
    private static readonly Guid Emp = Guid.NewGuid();
    private static readonly Guid Dept = Guid.NewGuid();

    private static ApplicationDbContext Ctx(string name, ICurrentUserService user) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options, user);

    private static IMapper Mapper() =>
        new MapperConfiguration(cfg => cfg.AddMaps(typeof(CreateQuickReportCommandHandler).Assembly)).CreateMapper();

    private static IReportFieldRegistry EmployeesRegistry() => new FakeRegistry(
        new[]
        {
            Reg.Field("employees.employeeNumber", Emp, "Employee", "EmployeeNumber"),
            Reg.Field("employees.departmentName", Dept, "Department", "NameAr",
                join: new[] { Reg.Step("Employee", "Department", "DepartmentId") }),
        },
        new[] { Reg.Subject("employees") });

    [Fact]
    public async Task Quick_create_persists_definition_with_auto_join()
    {
        var user = new FakeUser();
        await using var db = Ctx(nameof(Quick_create_persists_definition_with_auto_join), user);
        var ids = new FakeIds(("Employee", Emp), ("Department", Dept));
        var handler = new CreateQuickReportCommandHandler(db, Mapper(), EmployeesRegistry(), ids, user);

        var result = await handler.Handle(new CreateQuickReportCommand
        {
            NameAr = "دليل الموظفين",
            NameEn = "Employee Directory",
            FieldKeys = new() { "employees.employeeNumber", "employees.departmentName" },
        }, CancellationToken.None);

        result.Report.Code.Should().NotBeNullOrWhiteSpace();
        result.SkippedFieldKeys.Should().BeEmpty();
        result.UnknownKeys.Should().BeEmpty();

        var saved = await db.Set<ReportDefinition>()
            .Include(r => r.Fields).Include(r => r.Relationships)
            .SingleAsync();

        saved.PrimaryObjectId.Should().Be(Emp);
        saved.OwnerId.Should().Be(user.UserId);
        saved.IsPublished.Should().BeTrue();
        saved.Fields.Should().HaveCount(2);
        saved.Relationships.Should().ContainSingle();
        var rel = saved.Relationships.Single();
        rel.SourceObjectId.Should().Be(Emp);
        rel.TargetObjectId.Should().Be(Dept);
        rel.JoinField.Should().Be("DepartmentId");
        rel.JoinType.Should().Be("Left");
    }

    [Fact]
    public async Task Quick_create_reports_unknown_keys_without_failing()
    {
        var user = new FakeUser();
        await using var db = Ctx(nameof(Quick_create_reports_unknown_keys_without_failing), user);
        var ids = new FakeIds(("Employee", Emp), ("Department", Dept));
        var handler = new CreateQuickReportCommandHandler(db, Mapper(), EmployeesRegistry(), ids, user);

        var result = await handler.Handle(new CreateQuickReportCommand
        {
            NameAr = "تقرير", NameEn = "Report",
            FieldKeys = new() { "employees.employeeNumber", "employees.doesNotExist" },
        }, CancellationToken.None);

        result.UnknownKeys.Should().ContainSingle().Which.Should().Be("employees.doesNotExist");
        (await db.Set<ReportDefinition>().SingleAsync()).Fields.Should().HaveCount(1);
    }

    [Fact]
    public async Task Seed_system_reports_is_idempotent()
    {
        var user = new FakeUser();
        await using var db = Ctx(nameof(Seed_system_reports_is_idempotent), user);
        var ids = new FakeIds(("Employee", Emp), ("Department", Dept));
        var registry = EmployeesRegistry();
        var quick = new CreateQuickReportCommandHandler(db, Mapper(), registry, ids, user);
        var seed = new SeedSystemReportsCommandHandler(
            db, registry, user, new DirectSender(quick),
            NullLogger<SeedSystemReportsCommandHandler>.Instance);

        var first = await seed.Handle(new SeedSystemReportsCommand(), CancellationToken.None);
        first.Created.Should().Be(1);
        first.Codes.Should().ContainSingle().Which.Should().Be("SYS_EMPLOYEES");

        var second = await seed.Handle(new SeedSystemReportsCommand(), CancellationToken.None);
        second.Created.Should().Be(0, "the standard report already exists");
        second.Skipped.Should().Be(1);

        (await db.Set<ReportDefinition>().CountAsync()).Should().Be(1, "no duplicate standard reports");
    }
}
