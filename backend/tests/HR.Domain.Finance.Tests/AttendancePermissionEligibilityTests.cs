using HR.Application.Common.Interfaces;
using HR.Application.Engines.Attendance;
using HR.Application.Engines.Scope;
using HR.Domain.Engines.Attendance;
using HR.Domain.Engines.MasterData;
using HR.Infrastructure.Persistence;
using HR.Modules.Attendance.Services;
using HR.Modules.Employees.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>Unit tests for AttendancePermissionTypeService eligibility resolution.
/// Uses an in-memory ApplicationDbContext for MasterDataItems + AttendancePermission rows,
/// and a FakeScopeEngine keyed on the scope to isolate the service's membership logic.</summary>
public class AttendancePermissionEligibilityTests
{
    // ── Test infrastructure ──────────────────────────────────────────────────

    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid TenantId => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string? Email => "t@t.local";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext Ctx(string n) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(n).Options,
        new FakeUser());

    /// <summary>A fake IScopeEngine that returns pre-determined IncludedEmployeeIds for any scope passed to it.
    /// The predicate receives the scope and returns the set to include — used to cover all five eligibility cases.</summary>
    private sealed class FakeScopeEngine : IScopeEngine
    {
        private readonly Func<SelectionScope, IReadOnlyCollection<Guid>> _resolve;
        public FakeScopeEngine(Func<SelectionScope, IReadOnlyCollection<Guid>> resolve) => _resolve = resolve;

        public IReadOnlyList<ScopeDimensionInfo> Dimensions() => Array.Empty<ScopeDimensionInfo>();

        public Task<ScopeResolution> ResolveAsync(SelectionScope scope, CancellationToken ct)
        {
            var included = _resolve(scope);
            return Task.FromResult(new ScopeResolution(included, Array.Empty<ScopeExclusion>(), Array.Empty<string>()));
        }
    }

    /// <summary>A spy IScopeEngine that records whether ResolveAsync was ever invoked.
    /// Used to assert the service short-circuits before reaching the scope engine for null/All eligibility.</summary>
    private sealed class SpyScopeEngine : IScopeEngine
    {
        public bool WasCalled { get; private set; }
        public IReadOnlyList<ScopeDimensionInfo> Dimensions() => Array.Empty<ScopeDimensionInfo>();

        public Task<ScopeResolution> ResolveAsync(SelectionScope scope, CancellationToken ct)
        {
            WasCalled = true;
            // Return empty — if the service incorrectly uses this result, employee would be ineligible.
            return Task.FromResult(new ScopeResolution(Array.Empty<Guid>(), Array.Empty<ScopeExclusion>(), Array.Empty<string>()));
        }
    }

    /// <summary>Seed a MasterDataItem for AttendancePermissionType with optional eligibility scope in MetadataJson.</summary>
    private static async Task<MasterDataItem> SeedPermissionTypeAsync(
        ApplicationDbContext db, string code, SelectionScope? eligibility = null)
    {
        // Embed eligibility in the MetadataJson using PermissionTypeRules structure
        string? metadataJson = null;
        if (eligibility is not null)
        {
            var scopeJson = SelectionScopeJson.Serialize(eligibility);
            metadataJson = $"{{\"paid\":true,\"eligibility\":{scopeJson}}}";
        }

        var item = new MasterDataItem
        {
            ObjectType = MasterDataObjectType.AttendancePermissionType,
            Code = code,
            NameAr = $"إذن {code}",
            NameEn = $"Permission {code}",
            IsActive = true,
            MetadataJson = metadataJson,
        };
        db.MasterDataItems.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private static async Task<Guid> SeedEmployeeAsync(ApplicationDbContext db)
    {
        var emp = new Employee
        {
            EmployeeNumber = $"E-{Guid.NewGuid():N}",
            FirstName = "Ali", LastName = "Test",
            Email = $"{Guid.NewGuid():N}@t.local",
            BasicSalary = 5000m,
        };
        db.Employees.Add(emp);
        await db.SaveChangesAsync();
        return emp.Id;
    }

    // Inline-build the service with injected scope engine and db
    private static AttendancePermissionTypeService BuildService(ApplicationDbContext db, IScopeEngine scope)
        => new(db, scope);

    // ── Five eligibility cases ───────────────────────────────────────────────

    [Fact]
    public async Task Entire_company_type_is_eligible_for_everyone()
    {
        // Eligibility null → type has no eligibility filter → every employee is included,
        // AND the scope engine must NOT be called (Finding 2: short-circuit proof).
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        await SeedPermissionTypeAsync(db, "GENERAL"); // Eligibility = null
        var emp = await SeedEmployeeAsync(db);

        var spy = new SpyScopeEngine();
        var svc = BuildService(db, spy);

        var result = await svc.GetEligibleTypesAsync(emp, default);

        Assert.Single(result);
        Assert.Equal("GENERAL", result[0].Code);
        Assert.False(spy.WasCalled, "IScopeEngine.ResolveAsync must NOT be called when Eligibility is null");
    }

    [Fact]
    public async Task Mode_All_type_is_eligible_for_everyone()
    {
        // Eligibility.Mode == "All" → eligible for everyone; scope engine must NOT be called
        // (Finding 2: short-circuit proof — calling would be wasteful and the empty spy return
        // would wrongly make the employee ineligible if the short-circuit were missing).
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var allScope = SelectionScope.All(); // Mode = "All"
        await SeedPermissionTypeAsync(db, "ALL_TYPE", allScope);
        var emp = await SeedEmployeeAsync(db);

        var spy = new SpyScopeEngine();
        var svc = BuildService(db, spy);

        var result = await svc.GetEligibleTypesAsync(emp, default);

        Assert.Single(result);
        Assert.Equal("ALL_TYPE", result[0].Code);
        Assert.False(spy.WasCalled, "IScopeEngine.ResolveAsync must NOT be called when Mode==\"All\"");
    }

    [Fact]
    public async Task Department_scoped_type_only_for_that_department()
    {
        // Criteria mode, Department dimension → only employees in the dept.
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var deptId = Guid.NewGuid();
        var eligibility = new SelectionScope(
            "Criteria",
            new[] { new ScopeCriterion("Department", new[] { deptId }) },
            Array.Empty<ScopeCriterion>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>());
        await SeedPermissionTypeAsync(db, "DEPT_TYPE", eligibility);

        var inDeptEmp = await SeedEmployeeAsync(db);
        var outDeptEmp = await SeedEmployeeAsync(db);

        // Fake scope always returns only inDeptEmp, so outDeptEmp ∉ IncludedEmployeeIds.
        var inResult = await BuildService(db, new FakeScopeEngine(_ => new[] { inDeptEmp }))
            .GetEligibleTypesAsync(inDeptEmp, default);
        var outResult = await BuildService(db, new FakeScopeEngine(_ => new[] { inDeptEmp }))
            .GetEligibleTypesAsync(outDeptEmp, default);

        Assert.Single(inResult);
        Assert.Empty(outResult);
    }

    [Fact]
    public async Task Branch_scoped_type_only_for_that_branch()
    {
        // Criteria mode, Branch dimension.
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var branchId = Guid.NewGuid();
        var eligibility = new SelectionScope(
            "Criteria",
            new[] { new ScopeCriterion("Branch", new[] { branchId }) },
            Array.Empty<ScopeCriterion>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>());
        await SeedPermissionTypeAsync(db, "BRANCH_TYPE", eligibility);

        var inBranchEmp = await SeedEmployeeAsync(db);
        var outBranchEmp = await SeedEmployeeAsync(db);

        var inResult = await BuildService(db, new FakeScopeEngine(_ => new[] { inBranchEmp }))
            .GetEligibleTypesAsync(inBranchEmp, default);
        var outResult = await BuildService(db, new FakeScopeEngine(_ => new[] { inBranchEmp }))
            .GetEligibleTypesAsync(outBranchEmp, default);

        Assert.Single(inResult);
        Assert.Empty(outResult);
    }

    [Fact]
    public async Task Specific_employees_type_only_for_listed_ids()
    {
        // IncludeEmployeeIds: only those specific employee ids are eligible.
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var specificEmp = await SeedEmployeeAsync(db);
        var otherEmp = await SeedEmployeeAsync(db);

        var eligibility = new SelectionScope(
            "Criteria",
            Array.Empty<ScopeCriterion>(),
            Array.Empty<ScopeCriterion>(),
            new[] { specificEmp },  // explicit employee list
            Array.Empty<Guid>());
        await SeedPermissionTypeAsync(db, "SPECIFIC_TYPE", eligibility);

        var inResult = await BuildService(db, new FakeScopeEngine(_ => new[] { specificEmp }))
            .GetEligibleTypesAsync(specificEmp, default);
        var outResult = await BuildService(db, new FakeScopeEngine(_ => new[] { specificEmp }))
            .GetEligibleTypesAsync(otherEmp, default);

        Assert.Single(inResult);
        Assert.Empty(outResult);
    }

    [Fact]
    public async Task Excluded_employee_does_not_see_type()
    {
        // All minus ExcludeEmployeeIds: the excluded employee gets nothing.
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var excludedEmp = await SeedEmployeeAsync(db);
        var includedEmp = await SeedEmployeeAsync(db);

        var eligibility = new SelectionScope(
            "Criteria",
            Array.Empty<ScopeCriterion>(),
            Array.Empty<ScopeCriterion>(),
            Array.Empty<Guid>(),
            new[] { excludedEmp });  // ExcludeEmployeeIds
        await SeedPermissionTypeAsync(db, "EXCLUDE_TYPE", eligibility);

        // Scope engine already applied the exclusion: returns only includedEmp.
        var includedSet = new[] { includedEmp };

        var includedResult = await BuildService(db, new FakeScopeEngine(_ => includedSet))
            .GetEligibleTypesAsync(includedEmp, default);
        var excludedResult = await BuildService(db, new FakeScopeEngine(_ => includedSet))
            .GetEligibleTypesAsync(excludedEmp, default);

        Assert.Single(includedResult);
        Assert.Empty(excludedResult);
    }

    // ── Usage counts ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Usage_counts_todays_and_this_months_permissions()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var typeItem = await SeedPermissionTypeAsync(db, "GEN");
        var emp = await SeedEmployeeAsync(db);

        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // Two permissions this month (one today, one earlier this month) — stamped with the type.
        db.AttendancePermissions.Add(new AttendancePermission
        {
            EmployeeId = emp, Date = today,
            FromMinutes = 480, ToMinutes = 540, ExcusedMinutes = 60,
            PermissionTypeId = typeItem.Id,
            Source = AttendanceSources.AttendancePermission, RequestInstanceId = Guid.NewGuid(),
        });
        db.AttendancePermissions.Add(new AttendancePermission
        {
            EmployeeId = emp, Date = monthStart,
            FromMinutes = 480, ToMinutes = 570, ExcusedMinutes = 90,
            PermissionTypeId = typeItem.Id,
            Source = AttendanceSources.AttendancePermission, RequestInstanceId = Guid.NewGuid(),
        });
        // One permission last month (should NOT count).
        var lastMonth = monthStart.AddMonths(-1);
        db.AttendancePermissions.Add(new AttendancePermission
        {
            EmployeeId = emp, Date = lastMonth,
            FromMinutes = 480, ToMinutes = 540, ExcusedMinutes = 60,
            PermissionTypeId = typeItem.Id,
            Source = AttendanceSources.AttendancePermission, RequestInstanceId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        var svc = BuildService(db, new FakeScopeEngine(_ => Array.Empty<Guid>()));
        var result = await svc.GetEligibleTypesAsync(emp, default);

        Assert.Single(result);
        var usage = result[0].Usage;
        Assert.Equal(1, usage.UsedRequestsDay);
        Assert.Equal(60, usage.UsedMinutesDay);
        Assert.Equal(2, usage.UsedRequestsMonth);
        Assert.Equal(150, usage.UsedMinutesMonth); // 60 + 90
        Assert.Null(usage.RemainingMinutesDay);    // no type-level limit set (null rules)
        Assert.Null(usage.RemainingMinutesMonth);
        Assert.Null(usage.RemainingRequestsDay);
        Assert.Null(usage.RemainingRequestsMonth);
    }

    /// <summary>Regression for Finding 1 — cross-type contamination.
    /// Two types, employee has permissions for each. Each type must see ONLY its own rows.</summary>
    [Fact]
    public async Task Usage_per_type_is_isolated_no_cross_type_contamination()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var typeA = await SeedPermissionTypeAsync(db, "TYPE_A");
        var typeB = await SeedPermissionTypeAsync(db, "TYPE_B");
        var emp = await SeedEmployeeAsync(db);

        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        // 2 permissions for TYPE_A (60 + 30 minutes), 1 for TYPE_B (45 minutes) — all today.
        db.AttendancePermissions.Add(new AttendancePermission
        {
            EmployeeId = emp, Date = today,
            FromMinutes = 480, ToMinutes = 540, ExcusedMinutes = 60,
            PermissionTypeId = typeA.Id,
            Source = AttendanceSources.AttendancePermission, RequestInstanceId = Guid.NewGuid(),
        });
        db.AttendancePermissions.Add(new AttendancePermission
        {
            EmployeeId = emp, Date = monthStart,
            FromMinutes = 480, ToMinutes = 510, ExcusedMinutes = 30,
            PermissionTypeId = typeA.Id,
            Source = AttendanceSources.AttendancePermission, RequestInstanceId = Guid.NewGuid(),
        });
        db.AttendancePermissions.Add(new AttendancePermission
        {
            EmployeeId = emp, Date = today,
            FromMinutes = 600, ToMinutes = 645, ExcusedMinutes = 45,
            PermissionTypeId = typeB.Id,
            Source = AttendanceSources.AttendancePermission, RequestInstanceId = Guid.NewGuid(),
        });
        await db.SaveChangesAsync();

        var svc = BuildService(db, new FakeScopeEngine(_ => Array.Empty<Guid>()));
        var result = await svc.GetEligibleTypesAsync(emp, default);

        // Both types must appear.
        Assert.Equal(2, result.Count);

        var usageA = result.First(r => r.Code == "TYPE_A").Usage;
        var usageB = result.First(r => r.Code == "TYPE_B").Usage;

        // TYPE_A: 1 today-request (60 min today), 2 month-requests (90 min month total)
        Assert.Equal(1, usageA.UsedRequestsDay);
        Assert.Equal(60, usageA.UsedMinutesDay);
        Assert.Equal(2, usageA.UsedRequestsMonth);
        Assert.Equal(90, usageA.UsedMinutesMonth);

        // TYPE_B: 1 today-request (45 min today), 1 month-request (45 min month total)
        Assert.Equal(1, usageB.UsedRequestsDay);
        Assert.Equal(45, usageB.UsedMinutesDay);
        Assert.Equal(1, usageB.UsedRequestsMonth);
        Assert.Equal(45, usageB.UsedMinutesMonth);
    }

    // ── ResolveForRequestAsync ───────────────────────────────────────────────

    [Fact]
    public async Task ResolveForRequest_returns_context_by_code()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        // No eligibility filter → everyone is eligible (null eligibility → All).
        await SeedPermissionTypeAsync(db, "MYTYPE");
        var emp = await SeedEmployeeAsync(db);

        var svc = BuildService(db, new FakeScopeEngine(_ => Array.Empty<Guid>()));
        var ctx = await svc.ResolveForRequestAsync(emp, "MYTYPE", default);

        Assert.NotNull(ctx);
        Assert.Equal("MYTYPE", ctx!.Item.Code);
    }

    [Fact]
    public async Task ResolveForRequest_returns_null_for_unknown_code()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var emp = await SeedEmployeeAsync(db);

        var svc = BuildService(db, new FakeScopeEngine(_ => Array.Empty<Guid>()));
        var ctx = await svc.ResolveForRequestAsync(emp, "NONEXISTENT", default);

        Assert.Null(ctx);
    }

    /// <summary>Finding 4 — contract fix: ineligible employee must also get null from ResolveForRequestAsync.</summary>
    [Fact]
    public async Task ResolveForRequest_returns_null_when_employee_is_ineligible()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var deptId = Guid.NewGuid();
        // Type restricted to a department scope.
        var eligibility = new SelectionScope(
            "Criteria",
            new[] { new ScopeCriterion("Department", new[] { deptId }) },
            Array.Empty<ScopeCriterion>(),
            Array.Empty<Guid>(),
            Array.Empty<Guid>());
        await SeedPermissionTypeAsync(db, "DEPT_ONLY", eligibility);

        var ineligibleEmp = await SeedEmployeeAsync(db);
        var eligibleEmp = await SeedEmployeeAsync(db);

        // Scope engine returns only eligibleEmp; ineligibleEmp is excluded.
        var scope = new FakeScopeEngine(_ => new[] { eligibleEmp });

        var svc = BuildService(db, scope);
        var ctxIneligible = await svc.ResolveForRequestAsync(ineligibleEmp, "DEPT_ONLY", default);
        var ctxEligible = await svc.ResolveForRequestAsync(eligibleEmp, "DEPT_ONLY", default);

        Assert.Null(ctxIneligible);     // ineligible → null (Finding 4)
        Assert.NotNull(ctxEligible);    // eligible → context returned
        Assert.Equal("DEPT_ONLY", ctxEligible!.Item.Code);
    }
}
