# Reports Engine Phase 2 — Access Wiring, Sharing & Organization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the already-built `ReportAccessResolver` into every list/get/run path, add share-management endpoints, and add report organization (folders, tags, favorites/pinned/recent) so reports are access-controlled and organizable.

**Architecture:** Two deployable parts. **Part 1 (no schema change):** a pure visibility-predicate builder + an injectable `ReportAccessService` that resolves the caller's roles/department and enforces read/edit, wired into the three read handlers, plus share CRUD endpoints. **Part 2 (one migration `ReportOrganization`):** new `ReportFolder` / `ReportTag` / `ReportDefinitionTag` / `ReportUserState` entities and endpoints for folder CRUD, tag CRUD+assignment, favorite/pin toggles, recent tracking, and list view-filters.

**Tech Stack:** .NET 8, EF Core 8 (Npgsql), MediatR, AutoMapper, xUnit + FluentAssertions. PostgreSQL 16 (Azure). `dotnet-ef` 8.0.10.

## Global Constraints

- **No hardcoded report logic / no schema duplication.** Reuse `ReportAccessResolver`, `ReportShare`, `ReportAccessContext`, `ICurrentUserService`, `_db.UserRoles`, and the `Employee.UserId → DepartmentId` link. Do NOT introduce a parallel sharing table or a second access resolver.
- **Access resolver is the single source of truth.** The EF list predicate must mirror `ReportAccessResolver.CanRead` exactly: `owner OR Scope==Company OR a matching ReportShare (user/role/department)`. `ReportScope.Department`/`Shared` remain honored only via explicit shares (documented known limit — do not invent a `ReportDefinition.DepartmentId` column).
- **Every new endpoint is gated** by `[RequirePermission("Platform.Reports.<X>")]` (permissions already seeded: `View`, `Create`, `Edit`, `Delete`, `Export`) **plus** the access resolver. Share management + organization mutations require `Edit`; reads/toggles require `View`. No new permission strings are added.
- **Namespaces:** services in `HR.Modules.Platform.Services.Reports`; commands in `HR.Modules.Platform.Commands.Reports`; queries in `HR.Modules.Platform.Queries.Reports`; entities in `HR.Domain.Engines.Reports`; EF configs in `HR.Infrastructure.Persistence.Configurations.Engines`.
- **Entities:** `ReportFolder`, `ReportTag`, `ReportUserState` derive from `TenantEntity`; the join `ReportDefinitionTag` derives from `BaseEntity`. Table names use the `engine_report_*` prefix.
- **DbContext constructor is `ApplicationDbContext(options, ICurrentUserService)`** — tests that need a context construct it that way (see `ReportExecutionIntegrationTests`).
- **Tests:** pure logic gets DB-free xUnit tests over `List<T>.AsQueryable()`. DB-touching tests use `[SkippableFact]` gated on the `REPORTS_TEST_DB` env var (mirror `ReportExecutionIntegrationTests`). Test project: `backend/tests/HR.Modules.Platform.Tests`, folder `Reports/`.
- **Commit after each task.** Conventional commits: `feat(reports):` / `test(reports):`.

---

## File Structure

**Part 1 — Access wiring + sharing (no migration):**
- `backend/src/HR.Modules/Platform/Services/Reports/ReportVisibilityPredicate.cs` *(new)* — pure `Expression<Func<ReportDefinition,bool>>` builder mirroring `CanRead`.
- `backend/src/HR.Modules/Platform/Services/Reports/IReportAccessService.cs` + `ReportAccessService.cs` *(new)* — resolves caller context (roles+dept), exposes `FilterVisibleAsync`, `EnsureCanReadAsync`, `EnsureCanEditAsync`.
- `backend/src/HR.Modules/Platform/Queries/Reports/ReportQueries.cs` *(modify)* — wire access into `GetReportsQueryHandler`, `GetReportByIdQueryHandler`, `RunReportQueryHandler`.
- `backend/src/HR.Modules/Platform/Commands/Reports/ReportShareCommands.cs` *(new)* — add/remove share commands + handlers.
- `backend/src/HR.Modules/Platform/Queries/Reports/ReportShareQueries.cs` *(new)* — list shares query + handler.
- `backend/src/HR.Modules/Platform/Controllers/ReportsController.cs` *(modify)* — `{id}/shares` GET/POST/DELETE.
- `backend/src/HR.Modules/Platform/MappingProfiles/PlatformMappingProfile.cs` *(modify)* — `ReportShare → ReportShareDto`.
- DI registration file (see Task A2 for exact location discovered at implementation time).

**Part 2 — Organization (one migration `ReportOrganization`):**
- `backend/src/HR.Domain/Engines/Reports/ReportOrganization.cs` *(new)* — `ReportFolder`, `ReportTag`, `ReportDefinitionTag`, `ReportUserState`.
- `backend/src/HR.Domain/Engines/Reports/ReportDefinition.cs` *(modify)* — add `Guid? FolderId`.
- `backend/src/HR.Infrastructure/Persistence/Configurations/Engines/ReportOrganizationConfigurations.cs` *(new)* — EF configs.
- `backend/src/HR.Infrastructure/Persistence/ApplicationDbContext.cs` *(modify)* — DbSets.
- `backend/src/HR.Infrastructure/Migrations/*_ReportOrganization.cs` *(generated)*.
- `backend/src/HR.Modules/Platform/DTOs/Reports/ReportOrganizationDtos.cs` *(new)*.
- `backend/src/HR.Modules/Platform/Commands/Reports/ReportFolderCommands.cs`, `ReportTagCommands.cs`, `ReportUserStateCommands.cs` *(new)*.
- `backend/src/HR.Modules/Platform/Queries/Reports/ReportOrganizationQueries.cs` *(new)*.
- `backend/src/HR.Modules/Platform/Controllers/ReportsController.cs` *(modify)* — folders / tags / favorite / pin endpoints.
- `backend/src/HR.Modules/Platform/Queries/Reports/ReportQueries.cs` *(modify)* — `GetReportsQuery` gains `View`/`FolderId`/`TagId` filters + `RunReportQueryHandler` stamps `LastViewedAt`.

---

# PART 1 — Access Wiring & Sharing (no schema change; independently deployable)

## Task A1: Pure visibility predicate builder

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/Reports/ReportVisibilityPredicate.cs`
- Test: `backend/tests/HR.Modules.Platform.Tests/Reports/ReportVisibilityPredicateTests.cs`

**Interfaces:**
- Consumes: `ReportAccessContext` (existing: `UserId`, `DepartmentId?`, `RoleIds`), `ReportDefinition`, `ReportShare`, `ReportScope` (existing).
- Produces: `public static Expression<Func<ReportDefinition, bool>> ReportVisibilityPredicate.Build(ReportAccessContext ctx)` — a predicate usable in EF `.Where(...)` that returns true iff the caller can read the report. Requires `ReportDefinition.Shares` to be navigable (it is: `ICollection<ReportShare> Shares`).

- [ ] **Step 1: Write the failing test**

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~ReportVisibilityPredicateTests`
Expected: FAIL — `ReportVisibilityPredicate` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
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
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~ReportVisibilityPredicateTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Reports/ReportVisibilityPredicate.cs backend/tests/HR.Modules.Platform.Tests/Reports/ReportVisibilityPredicateTests.cs
git commit -m "feat(reports): EF-translatable visibility predicate mirroring access resolver"
```

---

## Task A2: `ReportAccessService` (context resolution + enforcement) + DI

**Files:**
- Create: `backend/src/HR.Modules/Platform/Services/Reports/IReportAccessService.cs`
- Create: `backend/src/HR.Modules/Platform/Services/Reports/ReportAccessService.cs`
- Modify: `backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs` — add the new registration next to the existing `services.AddScoped<...IReportObjectResolver, ...>()` / `services.AddScoped<...IReportExecutionService, ...>()` lines (~line 48–50).

**Interfaces:**
- Consumes: `ICurrentUserService` (`UserId`, `TenantId`), `ApplicationDbContext` (`_db.UserRoles`, `_db.Set<Employee>()`), `ReportVisibilityPredicate.Build`, `ReportAccessResolver.CanEdit`, `NotFoundException`, `ForbiddenException`.
- Produces:
  - `Task<ReportAccessContext> BuildContextAsync(CancellationToken ct)`
  - `Task<IQueryable<ReportDefinition>> FilterVisibleAsync(IQueryable<ReportDefinition> source, CancellationToken ct)`
  - `Task EnsureCanReadAsync(Guid reportId, CancellationToken ct)` — throws `NotFoundException` if absent, `ForbiddenException` if not readable.
  - `Task EnsureCanEditAsync(Guid reportId, CancellationToken ct)` — same, edit rule.

Note (VERIFIED): the 403 exception is `HR.Application.Common.Exceptions.ForbiddenException` (constructor takes a message string; mapped to `403` by `HR.Api/Middleware/ExceptionHandlingMiddleware.cs`). Use `throw new ForbiddenException("You do not have access to this report.");`. Do NOT use `ForbiddenAccessException` (does not exist). The MediatR no-response shape is `record X(...) : IRequest;` + `class XHandler : IRequestHandler<X>` with `public async Task Handle(...)` (VERIFIED against `DeleteReportFieldCommand`). EF configs are auto-applied via `ApplyConfigurationsFromAssembly` — no manual config registration is ever needed.

- [ ] **Step 1: Write the failing test**

This service is DB-touching; test it as a `[SkippableFact]` mirroring the integration-test harness (roles + employee-department seeding, transaction rollback).

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Reports;
using HR.Domain.Entities.Identity;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Reports;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Modules.Platform.Tests.Reports;

public class ReportAccessServiceTests
{
    private static string? Conn => Environment.GetEnvironmentVariable("REPORTS_TEST_DB");

    private sealed class StubUser : ICurrentUserService
    {
        public StubUser(Guid u, Guid t) { UserId = u; TenantId = t; }
        public Guid UserId { get; }
        public Guid TenantId { get; }
        public string? Email => "t@e.com";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    [SkippableFact]
    public async Task Context_includes_caller_roles_and_department()
    {
        Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
        var tenant = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var user = new StubUser(userId, tenant);
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Conn).Options;
        await using var db = new ApplicationDbContext(opts, user);
        await using var tx = await db.Database.BeginTransactionAsync();

        var roleId = Guid.NewGuid();
        db.UserRoles.Add(new UserRole { Id = Guid.NewGuid(), UserId = userId, RoleId = roleId });
        // Employee links this user to a department (Employee.UserId → DepartmentId).
        var deptId = Guid.NewGuid();
        db.Set<HR.Domain.Entities.Employees.Employee>().Add(new HR.Domain.Entities.Employees.Employee
        {
            Id = Guid.NewGuid(), EmployeeNumber = "A1", FirstName = "T", LastName = "U",
            Email = "a1@e.com", Gender = Gender.Male,
            DateOfBirth = new DateTime(1990,1,1,0,0,0,DateTimeKind.Utc),
            HireDate = new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc),
            Status = EmployeeStatus.Active, UserId = userId, DepartmentId = deptId,
        });
        await db.SaveChangesAsync();

        var svc = new ReportAccessService(db, user);
        var ctx = await svc.BuildContextAsync(default);

        ctx.UserId.Should().Be(userId);
        ctx.RoleIds.Should().Contain(roleId);
        ctx.DepartmentId.Should().Be(deptId);

        await tx.RollbackAsync();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~ReportAccessServiceTests`
Expected: FAIL — `ReportAccessService` does not exist (compile error). (Test itself skips at runtime without `REPORTS_TEST_DB`, but the build must fail first.)

- [ ] **Step 3: Write minimal implementation**

```csharp
// IReportAccessService.cs
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HR.Domain.Engines.Reports;

namespace HR.Modules.Platform.Services.Reports;

public interface IReportAccessService
{
    Task<ReportAccessContext> BuildContextAsync(CancellationToken ct);
    Task<IQueryable<ReportDefinition>> FilterVisibleAsync(IQueryable<ReportDefinition> source, CancellationToken ct);
    Task EnsureCanReadAsync(System.Guid reportId, CancellationToken ct);
    Task EnsureCanEditAsync(System.Guid reportId, CancellationToken ct);
}
```

```csharp
// ReportAccessService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HR.Application.Common.Interfaces;
using HR.Application.Common.Exceptions;
using HR.Domain.Engines.Reports;
using HR.Domain.Entities.Employees;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Reports;

public sealed class ReportAccessService : IReportAccessService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _user;

    public ReportAccessService(ApplicationDbContext db, ICurrentUserService user)
    { _db = db; _user = user; }

    public async Task<ReportAccessContext> BuildContextAsync(CancellationToken ct)
    {
        var uid = _user.UserId;
        var roleIds = await _db.UserRoles.Where(ur => ur.UserId == uid)
            .Select(ur => ur.RoleId).ToListAsync(ct);
        var deptId = await _db.Set<Employee>().Where(e => e.UserId == uid)
            .Select(e => e.DepartmentId).FirstOrDefaultAsync(ct);
        return new ReportAccessContext
        {
            UserId = uid,
            DepartmentId = deptId,
            RoleIds = new HashSet<Guid>(roleIds),
        };
    }

    public async Task<IQueryable<ReportDefinition>> FilterVisibleAsync(IQueryable<ReportDefinition> source, CancellationToken ct)
    {
        var ctx = await BuildContextAsync(ct);
        return source.Where(ReportVisibilityPredicate.Build(ctx));
    }

    public async Task EnsureCanReadAsync(Guid reportId, CancellationToken ct)
    {
        var (report, shares, ctx) = await LoadAsync(reportId, ct);
        if (!ReportAccessResolver.CanRead(report, shares, ctx))
            throw new ForbiddenException("You do not have access to this report.");
    }

    public async Task EnsureCanEditAsync(Guid reportId, CancellationToken ct)
    {
        var (report, shares, ctx) = await LoadAsync(reportId, ct);
        if (!ReportAccessResolver.CanEdit(report, shares, ctx))
            throw new ForbiddenException("You do not have permission to edit this report.");
    }

    private async Task<(ReportDefinition, IReadOnlyList<ReportShare>, ReportAccessContext)> LoadAsync(Guid reportId, CancellationToken ct)
    {
        var report = await _db.Set<ReportDefinition>().Include(r => r.Shares)
            .FirstOrDefaultAsync(r => r.Id == reportId, ct)
            ?? throw new NotFoundException("ReportDefinition", reportId);
        var ctx = await BuildContextAsync(ct);
        return (report, report.Shares.ToList(), ctx);
    }
}
```

- [ ] **Step 4: Register in DI**

In `backend/src/HR.Modules/Platform/DependencyInjection/DependencyInjection.cs`, next to the existing `IReportObjectResolver` / `IReportExecutionService` registrations (~line 48–50), add:

```csharp
        services.AddScoped<HR.Modules.Platform.Services.Reports.IReportAccessService,
            HR.Modules.Platform.Services.Reports.ReportAccessService>();
```

- [ ] **Step 5: Build + run test (skips cleanly)**

Run: `dotnet build backend/src/HR.Modules/HR.Modules.csproj` (or the solution) then
`dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~ReportAccessServiceTests`
Expected: BUILD succeeds; test **Skipped** locally (no `REPORTS_TEST_DB`), or PASS if the env var is set.

- [ ] **Step 6: Commit**

```bash
git add backend/src/HR.Modules/Platform/Services/Reports/IReportAccessService.cs backend/src/HR.Modules/Platform/Services/Reports/ReportAccessService.cs backend/tests/HR.Modules.Platform.Tests/Reports/ReportAccessServiceTests.cs
git add -A  # picks up the DI registration file
git commit -m "feat(reports): access service resolving caller roles/department + read/edit enforcement"
```

---

## Task A3: Wire access into list / get / run handlers

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Queries/Reports/ReportQueries.cs`

**Interfaces:**
- Consumes: `IReportAccessService` (from A2).
- Produces: no new public types; behavior change — `GetReportsQueryHandler` returns only visible reports; `GetReportByIdQueryHandler` and `RunReportQueryHandler` throw 403 when the caller cannot read.

- [ ] **Step 1: Write the failing test**

A `[SkippableFact]` verifying that a foreign personal report is excluded from the list. Add to `backend/tests/HR.Modules.Platform.Tests/Reports/ReportAccessServiceTests.cs`:

```csharp
[SkippableFact]
public async Task FilterVisible_excludes_foreign_personal_report()
{
    Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
    var tenant = Guid.NewGuid();
    var userId = Guid.NewGuid();
    var user = new StubUser(userId, tenant);
    var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Conn).Options;
    await using var db = new ApplicationDbContext(opts, user);
    await using var tx = await db.Database.BeginTransactionAsync();

    var mine = new ReportDefinition { Id = Guid.NewGuid(), TenantId = tenant, Code = "M"+Guid.NewGuid().ToString("N")[..6], NameEn="mine", NameAr="لي", OwnerId = userId, Scope = ReportScope.Personal, PrimaryObjectId = Guid.NewGuid() };
    var foreign = new ReportDefinition { Id = Guid.NewGuid(), TenantId = tenant, Code = "F"+Guid.NewGuid().ToString("N")[..6], NameEn="foreign", NameAr="غريب", OwnerId = Guid.NewGuid(), Scope = ReportScope.Personal, PrimaryObjectId = Guid.NewGuid() };
    db.Set<ReportDefinition>().AddRange(mine, foreign);
    await db.SaveChangesAsync();

    var svc = new ReportAccessService(db, user);
    var visible = await (await svc.FilterVisibleAsync(db.Set<ReportDefinition>(), default)).ToListAsync();

    visible.Select(r => r.Id).Should().Contain(mine.Id).And.NotContain(foreign.Id);
    await tx.RollbackAsync();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~FilterVisible_excludes_foreign_personal_report`
Expected: locally **Skipped**. With `REPORTS_TEST_DB` set it PASSES only after A2 (`FilterVisibleAsync`) exists — this test primarily guards the wiring below. (If A2 is complete, this already passes; its role is to lock the contract before editing handlers.)

- [ ] **Step 3: Wire the three handlers**

In `ReportQueries.cs`:

`RunReportQueryHandler` — inject `IReportAccessService` and enforce read before executing:

```csharp
public class RunReportQueryHandler : IRequestHandler<RunReportQuery, ReportResult>
{
    private readonly IReportExecutionService _exec;
    private readonly IReportAccessService _access;
    public RunReportQueryHandler(IReportExecutionService exec, IReportAccessService access)
    { _exec = exec; _access = access; }
    public async Task<ReportResult> Handle(RunReportQuery request, CancellationToken ct)
    {
        await _access.EnsureCanReadAsync(request.Id, ct);
        return await _exec.RunAsync(request.Id, request.Page, request.PageSize, ct);
    }
}
```

`GetReportsQueryHandler` — filter the base query through the access service (inject `IReportAccessService _access`; add the field + constructor param). Replace the `var query = ...AsQueryable();` line's downstream usage so the visibility filter is applied first:

```csharp
public class GetReportsQueryHandler : IRequestHandler<GetReportsQuery, PaginatedList<ReportDefinitionDto>>
{
    private readonly ApplicationDbContext _context; private readonly IMapper _mapper; private readonly IReportAccessService _access;
    public GetReportsQueryHandler(ApplicationDbContext context, IMapper mapper, IReportAccessService access)
    { _context = context; _mapper = mapper; _access = access; }
    public async Task<PaginatedList<ReportDefinitionDto>> Handle(GetReportsQuery request, CancellationToken ct)
    {
        var baseQuery = _context.Set<ReportDefinition>()
            .Include(r => r.Fields.OrderBy(f => f.SortOrder)).Include(r => r.Filters)
            .Include(r => r.Groupings).Include(r => r.Sortings).Include(r => r.Shares)
            .AsQueryable();
        var query = await _access.FilterVisibleAsync(baseQuery, ct);
        if (!string.IsNullOrEmpty(request.Search))
            query = query.Where(r => r.NameEn.Contains(request.Search) || r.NameAr.Contains(request.Search));
        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(r => r.CreatedAt)
            .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return new PaginatedList<ReportDefinitionDto> { Items = _mapper.Map<List<ReportDefinitionDto>>(items), PageNumber = request.PageNumber, PageSize = request.PageSize, TotalCount = totalCount };
    }
}
```

`GetReportByIdQueryHandler` — inject `IReportAccessService _access` and call `await _access.EnsureCanReadAsync(request.Id, ct);` **before** loading/returning the DTO (place it as the first line of `Handle`; it also throws `NotFoundException` for missing reports so the existing not-found behavior is preserved).

- [ ] **Step 4: Build + full test run**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests`
Expected: BUILD succeeds; all existing tests PASS; new access tests Skipped locally.

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Queries/Reports/ReportQueries.cs backend/tests/HR.Modules.Platform.Tests/Reports/ReportAccessServiceTests.cs
git commit -m "feat(reports): gate list/get/run through the access resolver"
```

---

## Task B1: Share-management commands + query + handlers

**Files:**
- Create: `backend/src/HR.Modules/Platform/Commands/Reports/ReportShareCommands.cs`
- Create: `backend/src/HR.Modules/Platform/Queries/Reports/ReportShareQueries.cs`
- Modify: `backend/src/HR.Modules/Platform/MappingProfiles/PlatformMappingProfile.cs`

**Interfaces:**
- Consumes: `IReportAccessService.EnsureCanEditAsync`, `ApplicationDbContext.ReportShares`, `ReportShareDto` (existing), `IMapper`.
- Produces:
  - `record AddReportShareCommand(Guid ReportDefinitionId, Guid? SharedWithUserId, Guid? SharedWithRoleId, Guid? SharedWithDepartmentId, bool CanEdit) : IRequest<ReportShareDto>`
  - `record RemoveReportShareCommand(Guid ReportDefinitionId, Guid ShareId) : IRequest`
  - `record GetReportSharesQuery(Guid ReportDefinitionId) : IRequest<List<ReportShareDto>>`

- [ ] **Step 1: Write the failing test**

`[SkippableFact]` in a new `backend/tests/HR.Modules.Platform.Tests/Reports/ReportShareCommandTests.cs`. Assert an owner can add a share and it round-trips through the list query. (Mirror the harness from A2; construct the handler directly with `db`, `mapper`, and a real `ReportAccessService`.) Build the mapper via `new MapperConfiguration(c => c.AddProfile<HR.Modules.Platform.MappingProfiles.PlatformMappingProfile>()).CreateMapper()`.

```csharp
[SkippableFact]
public async Task Owner_can_add_and_list_a_user_share()
{
    Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
    var tenant = Guid.NewGuid(); var owner = Guid.NewGuid();
    var user = new StubUser(owner, tenant);
    var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Conn).Options;
    await using var db = new ApplicationDbContext(opts, user);
    await using var tx = await db.Database.BeginTransactionAsync();

    var report = new ReportDefinition { Id = Guid.NewGuid(), TenantId = tenant, Code="S"+Guid.NewGuid().ToString("N")[..6], NameEn="r", NameAr="ر", OwnerId = owner, Scope = ReportScope.Personal, PrimaryObjectId = Guid.NewGuid() };
    db.Set<ReportDefinition>().Add(report); await db.SaveChangesAsync();

    var mapper = new AutoMapper.MapperConfiguration(c => c.AddProfile<HR.Modules.Platform.MappingProfiles.PlatformMappingProfile>()).CreateMapper();
    var access = new ReportAccessService(db, user);
    var shareWith = Guid.NewGuid();

    var added = await new AddReportShareCommandHandler(db, mapper, access)
        .Handle(new AddReportShareCommand(report.Id, shareWith, null, null, true), default);
    added.SharedWithUserId.Should().Be(shareWith);

    var list = await new GetReportSharesQueryHandler(db, mapper, access)
        .Handle(new GetReportSharesQuery(report.Id), default);
    list.Should().ContainSingle(s => s.SharedWithUserId == shareWith && s.CanEdit);

    await tx.RollbackAsync();
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~ReportShareCommandTests`
Expected: FAIL — handlers/commands do not exist (compile error).

- [ ] **Step 3: Write the commands + query + handlers**

```csharp
// ReportShareCommands.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using HR.Application.Common.Exceptions;
using HR.Domain.Engines.Reports;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.DTOs.Reports;
using HR.Modules.Platform.Services.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Commands.Reports;

public record AddReportShareCommand(Guid ReportDefinitionId, Guid? SharedWithUserId, Guid? SharedWithRoleId, Guid? SharedWithDepartmentId, bool CanEdit) : IRequest<ReportShareDto>;
public record RemoveReportShareCommand(Guid ReportDefinitionId, Guid ShareId) : IRequest;

public class AddReportShareCommandHandler : IRequestHandler<AddReportShareCommand, ReportShareDto>
{
    private readonly ApplicationDbContext _db; private readonly IMapper _mapper; private readonly IReportAccessService _access;
    public AddReportShareCommandHandler(ApplicationDbContext db, IMapper mapper, IReportAccessService access) { _db = db; _mapper = mapper; _access = access; }
    public async Task<ReportShareDto> Handle(AddReportShareCommand r, CancellationToken ct)
    {
        await _access.EnsureCanEditAsync(r.ReportDefinitionId, ct);
        if (r.SharedWithUserId is null && r.SharedWithRoleId is null && r.SharedWithDepartmentId is null)
            throw new ValidationException("A share must target a user, role, or department.");
        var entity = new ReportShare
        {
            Id = Guid.NewGuid(), ReportDefinitionId = r.ReportDefinitionId,
            SharedWithUserId = r.SharedWithUserId, SharedWithRoleId = r.SharedWithRoleId,
            SharedWithDepartmentId = r.SharedWithDepartmentId, CanEdit = r.CanEdit, SharedAt = DateTime.UtcNow,
        };
        _db.ReportShares.Add(entity);
        await _db.SaveChangesAsync(ct);
        return _mapper.Map<ReportShareDto>(entity);
    }
}

public class RemoveReportShareCommandHandler : IRequestHandler<RemoveReportShareCommand>
{
    private readonly ApplicationDbContext _db; private readonly IReportAccessService _access;
    public RemoveReportShareCommandHandler(ApplicationDbContext db, IReportAccessService access) { _db = db; _access = access; }
    public async Task Handle(RemoveReportShareCommand r, CancellationToken ct)
    {
        await _access.EnsureCanEditAsync(r.ReportDefinitionId, ct);
        var entity = await _db.ReportShares.FirstOrDefaultAsync(s => s.Id == r.ShareId && s.ReportDefinitionId == r.ReportDefinitionId, ct)
            ?? throw new NotFoundException("ReportShare", r.ShareId);
        _db.ReportShares.Remove(entity);
        await _db.SaveChangesAsync(ct);
    }
}
```

```csharp
// ReportShareQueries.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.DTOs.Reports;
using HR.Modules.Platform.Services.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Queries.Reports;

public record GetReportSharesQuery(Guid ReportDefinitionId) : IRequest<List<ReportShareDto>>;

public class GetReportSharesQueryHandler : IRequestHandler<GetReportSharesQuery, List<ReportShareDto>>
{
    private readonly ApplicationDbContext _db; private readonly IMapper _mapper; private readonly IReportAccessService _access;
    public GetReportSharesQueryHandler(ApplicationDbContext db, IMapper mapper, IReportAccessService access) { _db = db; _mapper = mapper; _access = access; }
    public async Task<List<ReportShareDto>> Handle(GetReportSharesQuery q, CancellationToken ct)
    {
        await _access.EnsureCanReadAsync(q.ReportDefinitionId, ct);
        var shares = await _db.ReportShares.Where(s => s.ReportDefinitionId == q.ReportDefinitionId).ToListAsync(ct);
        return _mapper.Map<List<ReportShareDto>>(shares);
    }
}
```

Note: `RemoveReportShareCommandHandler : IRequestHandler<RemoveReportShareCommand>` (no response). Confirm the project's MediatR major version: if `IRequestHandler<T>` requires `Task<Unit>`, return `Unit.Value` and change the signature accordingly — check an existing no-response handler such as `DeleteReportFieldCommand`'s handler for the exact shape and copy it. If `ValidationException`/`NotFoundException` constructors differ, match existing usages in `ReportCommands.cs`.

- [ ] **Step 4: Add the AutoMapper mapping**

In `PlatformMappingProfile.cs`, in the `// Reports` block (after the existing report maps), add:

```csharp
CreateMap<ReportShare, ReportShareDto>();
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~ReportShareCommandTests`
Expected: locally **Skipped**; PASS with `REPORTS_TEST_DB` set. BUILD must succeed.

- [ ] **Step 6: Commit**

```bash
git add backend/src/HR.Modules/Platform/Commands/Reports/ReportShareCommands.cs backend/src/HR.Modules/Platform/Queries/Reports/ReportShareQueries.cs backend/src/HR.Modules/Platform/MappingProfiles/PlatformMappingProfile.cs backend/tests/HR.Modules.Platform.Tests/Reports/ReportShareCommandTests.cs
git commit -m "feat(reports): share add/remove/list commands gated by CanEdit"
```

---

## Task B2: Share endpoints on the controller

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Controllers/ReportsController.cs`

**Interfaces:**
- Consumes: `AddReportShareCommand`, `RemoveReportShareCommand`, `GetReportSharesQuery` (B1).
- Produces: `GET /{id}/shares`, `POST /{id}/shares`, `DELETE /{id}/shares/{shareId}`.

- [ ] **Step 1: Add the endpoints**

Add to `ReportsController` (import `HR.Modules.Platform.Queries.Reports` is already present via the queries namespace; add the commands namespace usings if missing):

```csharp
// Shares
[HttpGet("{id:guid}/shares")]
[RequirePermission("Platform.Reports.View")]
public async Task<ActionResult<ApiResponse<List<ReportShareDto>>>> GetShares(Guid id, CancellationToken ct)
{ var result = await Mediator.Send(new GetReportSharesQuery(id), ct); return OkResponse(result); }

[HttpPost("{id:guid}/shares")]
[RequirePermission("Platform.Reports.Edit")]
public async Task<ActionResult<ApiResponse<ReportShareDto>>> AddShare(Guid id, [FromBody] AddReportShareCommand command, CancellationToken ct)
{ var result = await Mediator.Send(command with { ReportDefinitionId = id }, ct); return CreatedResponse(result); }

[HttpDelete("{id:guid}/shares/{shareId:guid}")]
[RequirePermission("Platform.Reports.Edit")]
public async Task<ActionResult<ApiResponse>> RemoveShare(Guid id, Guid shareId, CancellationToken ct)
{ await Mediator.Send(new RemoveReportShareCommand(id, shareId), ct); return OkResponse("Share removed"); }
```

Add `using HR.Modules.Platform.Queries.Reports;` if not already imported (it is used by `GetReportSharesQuery`). Ensure `List<ReportShareDto>` resolves (the DTO namespace is already imported).

- [ ] **Step 2: Build**

Run: `dotnet build backend/src/HR.Api/HR.Api.csproj`
Expected: BUILD succeeds.

- [ ] **Step 3: Commit**

```bash
git add backend/src/HR.Modules/Platform/Controllers/ReportsController.cs
git commit -m "feat(reports): share management endpoints ({id}/shares GET/POST/DELETE)"
```

**► PART 1 CHECKPOINT:** Part 1 is independently deployable (no migration). Run the full suite (`dotnet test backend/tests/HR.Modules.Platform.Tests`) and, if desired, deploy before starting Part 2.

---

# PART 2 — Organization (migration `ReportOrganization`)

## Task C1: Organization entities + `FolderId` + EF configs + DbSets

**Files:**
- Create: `backend/src/HR.Domain/Engines/Reports/ReportOrganization.cs`
- Modify: `backend/src/HR.Domain/Engines/Reports/ReportDefinition.cs` (add `Guid? FolderId`)
- Create: `backend/src/HR.Infrastructure/Persistence/Configurations/Engines/ReportOrganizationConfigurations.cs`
- Modify: `backend/src/HR.Infrastructure/Persistence/ApplicationDbContext.cs`

**Interfaces:**
- Produces (consumed by C2/D1–D3):
  - `ReportFolder : TenantEntity` — `string NameEn`, `string NameAr`, `Guid? ParentFolderId`.
  - `ReportTag : TenantEntity` — `string Name`, `string? Color`.
  - `ReportDefinitionTag : BaseEntity` — `Guid ReportDefinitionId`, `Guid ReportTagId`.
  - `ReportUserState : TenantEntity` — `Guid UserId`, `Guid ReportDefinitionId`, `bool IsFavorite`, `bool IsPinned`, `DateTime? LastViewedAt`.
  - `ReportDefinition.FolderId` (`Guid?`).
  - DbSets: `ReportFolders`, `ReportTags`, `ReportDefinitionTags`, `ReportUserStates`.

- [ ] **Step 1: Write the entities**

```csharp
// ReportOrganization.cs
using System;
using HR.Domain.Common;

namespace HR.Domain.Engines.Reports;

public class ReportFolder : TenantEntity
{
    public string NameEn { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public Guid? ParentFolderId { get; set; }
}

public class ReportTag : TenantEntity
{
    public string Name { get; set; } = null!;
    public string? Color { get; set; }
}

public class ReportDefinitionTag : BaseEntity
{
    public Guid ReportDefinitionId { get; set; }
    public Guid ReportTagId { get; set; }
}

public class ReportUserState : TenantEntity
{
    public Guid UserId { get; set; }
    public Guid ReportDefinitionId { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsPinned { get; set; }
    public DateTime? LastViewedAt { get; set; }
}
```

- [ ] **Step 2: Add `FolderId` to `ReportDefinition`**

In `ReportDefinition.cs`, add after `public Guid? TemplateId { get; set; }`:

```csharp
    public Guid? FolderId { get; set; }
```

- [ ] **Step 3: Write the EF configurations**

```csharp
// ReportOrganizationConfigurations.cs
using HR.Domain.Engines.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Infrastructure.Persistence.Configurations.Engines;

public class ReportFolderConfiguration : IEntityTypeConfiguration<ReportFolder>
{
    public void Configure(EntityTypeBuilder<ReportFolder> b)
    {
        b.ToTable("engine_report_folders");
        b.HasKey(x => x.Id);
        b.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        b.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.ParentFolderId });
    }
}

public class ReportTagConfiguration : IEntityTypeConfiguration<ReportTag>
{
    public void Configure(EntityTypeBuilder<ReportTag> b)
    {
        b.ToTable("engine_report_tags");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Color).HasMaxLength(20);
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}

public class ReportDefinitionTagConfiguration : IEntityTypeConfiguration<ReportDefinitionTag>
{
    public void Configure(EntityTypeBuilder<ReportDefinitionTag> b)
    {
        b.ToTable("engine_report_definition_tags");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.ReportDefinitionId, x.ReportTagId }).IsUnique();
    }
}

public class ReportUserStateConfiguration : IEntityTypeConfiguration<ReportUserState>
{
    public void Configure(EntityTypeBuilder<ReportUserState> b)
    {
        b.ToTable("engine_report_user_states");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TenantId, x.UserId, x.ReportDefinitionId }).IsUnique();
    }
}
```

- [ ] **Step 4: Register DbSets**

In `ApplicationDbContext.cs`, in the `// Report Engine` block (after `ReportShares`), add:

```csharp
    public DbSet<ReportFolder> ReportFolders => Set<ReportFolder>();
    public DbSet<ReportTag> ReportTags => Set<ReportTag>();
    public DbSet<ReportDefinitionTag> ReportDefinitionTags => Set<ReportDefinitionTag>();
    public DbSet<ReportUserState> ReportUserStates => Set<ReportUserState>();
```

Confirm configurations are auto-applied (the context likely calls `ApplyConfigurationsFromAssembly` — verify with `grep -n "ApplyConfigurationsFromAssembly\|ApplyConfiguration" backend/src/HR.Infrastructure/Persistence/ApplicationDbContext.cs`). If configs are registered individually, add the four new ones there.

- [ ] **Step 5: Build**

Run: `dotnet build backend/src/HR.Infrastructure/HR.Infrastructure.csproj`
Expected: BUILD succeeds.

- [ ] **Step 6: Commit**

```bash
git add backend/src/HR.Domain/Engines/Reports/ReportOrganization.cs backend/src/HR.Domain/Engines/Reports/ReportDefinition.cs backend/src/HR.Infrastructure/Persistence/Configurations/Engines/ReportOrganizationConfigurations.cs backend/src/HR.Infrastructure/Persistence/ApplicationDbContext.cs
git commit -m "feat(reports): organization entities (folder/tag/tag-join/user-state) + FolderId"
```

---

## Task C2: Generate the `ReportOrganization` migration

**Files:**
- Generated: `backend/src/HR.Infrastructure/Migrations/*_ReportOrganization.cs` (+ `.Designer.cs` + snapshot update)

- [ ] **Step 1: Generate the migration**

Run (from the repo root; the EF startup project is `HR.Api`):

```bash
dotnet ef migrations add ReportOrganization --project backend/src/HR.Infrastructure --startup-project backend/src/HR.Api --context ApplicationDbContext
```

Expected: creates the migration + updates `ApplicationDbContextModelSnapshot.cs`.

- [ ] **Step 2: Inspect the generated `Up()`**

Open the new `*_ReportOrganization.cs` and confirm it creates `engine_report_folders`, `engine_report_tags`, `engine_report_definition_tags`, `engine_report_user_states`, adds the `FolderId` column to `engine_report_definitions`, and creates the unique indexes. It must contain **no** unrelated table changes (if it does, a prior model drift exists — stop and report).

- [ ] **Step 3: Build to confirm the migration compiles**

Run: `dotnet build backend/src/HR.Infrastructure/HR.Infrastructure.csproj`
Expected: BUILD succeeds.

- [ ] **Step 4: Commit**

```bash
git add backend/src/HR.Infrastructure/Migrations/
git commit -m "feat(reports): ReportOrganization migration (folders/tags/user-state + FolderId)"
```

> **DB apply is deferred to deployment** (Azure Postgres). Do NOT run `database update` against production here; the executing session applies it during the deploy step with the Key Vault password (`secretpulse/hrcloud-db-password`).

---

## Task D1: Folder CRUD (commands, queries, endpoints)

**Files:**
- Create: `backend/src/HR.Modules/Platform/DTOs/Reports/ReportOrganizationDtos.cs`
- Create: `backend/src/HR.Modules/Platform/Commands/Reports/ReportFolderCommands.cs`
- Create: `backend/src/HR.Modules/Platform/Queries/Reports/ReportOrganizationQueries.cs`
- Modify: `backend/src/HR.Modules/Platform/MappingProfiles/PlatformMappingProfile.cs`
- Modify: `backend/src/HR.Modules/Platform/Controllers/ReportsController.cs`

**Interfaces:**
- Produces:
  - `class ReportFolderDto { Guid Id; string NameEn; string NameAr; Guid? ParentFolderId; }`
  - `record CreateReportFolderCommand(string NameEn, string NameAr, Guid? ParentFolderId) : IRequest<ReportFolderDto>`
  - `record UpdateReportFolderCommand(Guid Id, string NameEn, string NameAr, Guid? ParentFolderId) : IRequest<ReportFolderDto>`
  - `record DeleteReportFolderCommand(Guid Id) : IRequest`
  - `record GetReportFoldersQuery() : IRequest<List<ReportFolderDto>>`

- [ ] **Step 1: Write the DTO**

```csharp
// ReportOrganizationDtos.cs
using System;

namespace HR.Modules.Platform.DTOs.Reports;

public class ReportFolderDto
{
    public Guid Id { get; set; }
    public string NameEn { get; set; } = null!;
    public string NameAr { get; set; } = null!;
    public Guid? ParentFolderId { get; set; }
}

public class ReportTagDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Color { get; set; }
}
```

- [ ] **Step 2: Write folder commands + query handlers**

```csharp
// ReportFolderCommands.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using HR.Application.Common.Exceptions;
using HR.Domain.Engines.Reports;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.DTOs.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Commands.Reports;

public record CreateReportFolderCommand(string NameEn, string NameAr, Guid? ParentFolderId) : IRequest<ReportFolderDto>;
public record UpdateReportFolderCommand(Guid Id, string NameEn, string NameAr, Guid? ParentFolderId) : IRequest<ReportFolderDto>;
public record DeleteReportFolderCommand(Guid Id) : IRequest;

public class CreateReportFolderCommandHandler : IRequestHandler<CreateReportFolderCommand, ReportFolderDto>
{
    private readonly ApplicationDbContext _db; private readonly IMapper _mapper;
    public CreateReportFolderCommandHandler(ApplicationDbContext db, IMapper mapper) { _db = db; _mapper = mapper; }
    public async Task<ReportFolderDto> Handle(CreateReportFolderCommand r, CancellationToken ct)
    {
        var e = new ReportFolder { Id = Guid.NewGuid(), NameEn = r.NameEn, NameAr = r.NameAr, ParentFolderId = r.ParentFolderId };
        _db.ReportFolders.Add(e); await _db.SaveChangesAsync(ct);
        return _mapper.Map<ReportFolderDto>(e);
    }
}

public class UpdateReportFolderCommandHandler : IRequestHandler<UpdateReportFolderCommand, ReportFolderDto>
{
    private readonly ApplicationDbContext _db; private readonly IMapper _mapper;
    public UpdateReportFolderCommandHandler(ApplicationDbContext db, IMapper mapper) { _db = db; _mapper = mapper; }
    public async Task<ReportFolderDto> Handle(UpdateReportFolderCommand r, CancellationToken ct)
    {
        var e = await _db.ReportFolders.FirstOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("ReportFolder", r.Id);
        e.NameEn = r.NameEn; e.NameAr = r.NameAr; e.ParentFolderId = r.ParentFolderId;
        await _db.SaveChangesAsync(ct);
        return _mapper.Map<ReportFolderDto>(e);
    }
}

public class DeleteReportFolderCommandHandler : IRequestHandler<DeleteReportFolderCommand>
{
    private readonly ApplicationDbContext _db;
    public DeleteReportFolderCommandHandler(ApplicationDbContext db) { _db = db; }
    public async Task Handle(DeleteReportFolderCommand r, CancellationToken ct)
    {
        var e = await _db.ReportFolders.FirstOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("ReportFolder", r.Id);
        // Detach any reports currently in this folder (leave them, just unfile).
        var reports = await _db.Set<ReportDefinition>().Where(rd => rd.FolderId == r.Id).ToListAsync(ct);
        foreach (var rd in reports) rd.FolderId = null;
        _db.ReportFolders.Remove(e);
        await _db.SaveChangesAsync(ct);
    }
}
```

```csharp
// ReportOrganizationQueries.cs  (folders portion; tags/user-state queries appended in D2/D3)
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.DTOs.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Queries.Reports;

public record GetReportFoldersQuery : IRequest<List<ReportFolderDto>>;

public class GetReportFoldersQueryHandler : IRequestHandler<GetReportFoldersQuery, List<ReportFolderDto>>
{
    private readonly ApplicationDbContext _db; private readonly IMapper _mapper;
    public GetReportFoldersQueryHandler(ApplicationDbContext db, IMapper mapper) { _db = db; _mapper = mapper; }
    public async Task<List<ReportFolderDto>> Handle(GetReportFoldersQuery q, CancellationToken ct)
        => _mapper.Map<List<ReportFolderDto>>(await _db.ReportFolders.OrderBy(f => f.NameEn).ToListAsync(ct));
}
```

Match the no-response handler shape to the project's MediatR version (see the B1 note about `IRequestHandler<T>` vs `Task<Unit>`).

- [ ] **Step 3: Add mappings**

In `PlatformMappingProfile.cs` Reports block:

```csharp
CreateMap<ReportFolder, ReportFolderDto>();
CreateMap<ReportTag, ReportTagDto>();
```

- [ ] **Step 4: Add folder endpoints to the controller**

```csharp
// Folders
[HttpGet("folders")]
[RequirePermission("Platform.Reports.View")]
public async Task<ActionResult<ApiResponse<List<ReportFolderDto>>>> GetFolders(CancellationToken ct)
{ var result = await Mediator.Send(new GetReportFoldersQuery(), ct); return OkResponse(result); }

[HttpPost("folders")]
[RequirePermission("Platform.Reports.Edit")]
public async Task<ActionResult<ApiResponse<ReportFolderDto>>> CreateFolder([FromBody] CreateReportFolderCommand command, CancellationToken ct)
{ var result = await Mediator.Send(command, ct); return CreatedResponse(result); }

[HttpPut("folders/{folderId:guid}")]
[RequirePermission("Platform.Reports.Edit")]
public async Task<ActionResult<ApiResponse<ReportFolderDto>>> UpdateFolder(Guid folderId, [FromBody] UpdateReportFolderCommand command, CancellationToken ct)
{ if (folderId != command.Id) return BadRequest(); var result = await Mediator.Send(command, ct); return OkResponse(result); }

[HttpDelete("folders/{folderId:guid}")]
[RequirePermission("Platform.Reports.Edit")]
public async Task<ActionResult<ApiResponse>> DeleteFolder(Guid folderId, CancellationToken ct)
{ await Mediator.Send(new DeleteReportFolderCommand(folderId), ct); return OkResponse("Folder deleted"); }
```

- [ ] **Step 5: Build**

Run: `dotnet build backend/src/HR.Api/HR.Api.csproj`
Expected: BUILD succeeds.

- [ ] **Step 6: Commit**

```bash
git add backend/src/HR.Modules/Platform/DTOs/Reports/ReportOrganizationDtos.cs backend/src/HR.Modules/Platform/Commands/Reports/ReportFolderCommands.cs backend/src/HR.Modules/Platform/Queries/Reports/ReportOrganizationQueries.cs backend/src/HR.Modules/Platform/MappingProfiles/PlatformMappingProfile.cs backend/src/HR.Modules/Platform/Controllers/ReportsController.cs
git commit -m "feat(reports): folder CRUD (commands, query, endpoints)"
```

---

## Task D2: Tag CRUD + assign/unassign

**Files:**
- Modify: `backend/src/HR.Modules/Platform/Commands/Reports/` — add `ReportTagCommands.cs`
- Modify: `backend/src/HR.Modules/Platform/Queries/Reports/ReportOrganizationQueries.cs` — add `GetReportTagsQuery`
- Modify: `backend/src/HR.Modules/Platform/Controllers/ReportsController.cs`

**Interfaces:**
- Produces:
  - `record CreateReportTagCommand(string Name, string? Color) : IRequest<ReportTagDto>`
  - `record DeleteReportTagCommand(Guid Id) : IRequest`
  - `record AssignReportTagCommand(Guid ReportDefinitionId, Guid ReportTagId) : IRequest`
  - `record UnassignReportTagCommand(Guid ReportDefinitionId, Guid ReportTagId) : IRequest`
  - `record GetReportTagsQuery() : IRequest<List<ReportTagDto>>`

- [ ] **Step 1: Write the tag commands**

```csharp
// ReportTagCommands.cs
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using HR.Application.Common.Exceptions;
using HR.Domain.Engines.Reports;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.DTOs.Reports;
using HR.Modules.Platform.Services.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Commands.Reports;

public record CreateReportTagCommand(string Name, string? Color) : IRequest<ReportTagDto>;
public record DeleteReportTagCommand(Guid Id) : IRequest;
public record AssignReportTagCommand(Guid ReportDefinitionId, Guid ReportTagId) : IRequest;
public record UnassignReportTagCommand(Guid ReportDefinitionId, Guid ReportTagId) : IRequest;

public class CreateReportTagCommandHandler : IRequestHandler<CreateReportTagCommand, ReportTagDto>
{
    private readonly ApplicationDbContext _db; private readonly IMapper _mapper;
    public CreateReportTagCommandHandler(ApplicationDbContext db, IMapper mapper) { _db = db; _mapper = mapper; }
    public async Task<ReportTagDto> Handle(CreateReportTagCommand r, CancellationToken ct)
    {
        var e = new ReportTag { Id = Guid.NewGuid(), Name = r.Name, Color = r.Color };
        _db.ReportTags.Add(e); await _db.SaveChangesAsync(ct);
        return _mapper.Map<ReportTagDto>(e);
    }
}

public class DeleteReportTagCommandHandler : IRequestHandler<DeleteReportTagCommand>
{
    private readonly ApplicationDbContext _db;
    public DeleteReportTagCommandHandler(ApplicationDbContext db) { _db = db; }
    public async Task Handle(DeleteReportTagCommand r, CancellationToken ct)
    {
        var e = await _db.ReportTags.FirstOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw new NotFoundException("ReportTag", r.Id);
        var links = _db.ReportDefinitionTags.Where(l => l.ReportTagId == r.Id);
        _db.ReportDefinitionTags.RemoveRange(links);
        _db.ReportTags.Remove(e);
        await _db.SaveChangesAsync(ct);
    }
}

public class AssignReportTagCommandHandler : IRequestHandler<AssignReportTagCommand>
{
    private readonly ApplicationDbContext _db; private readonly IReportAccessService _access;
    public AssignReportTagCommandHandler(ApplicationDbContext db, IReportAccessService access) { _db = db; _access = access; }
    public async Task Handle(AssignReportTagCommand r, CancellationToken ct)
    {
        await _access.EnsureCanEditAsync(r.ReportDefinitionId, ct);
        var exists = await _db.ReportDefinitionTags.AnyAsync(l => l.ReportDefinitionId == r.ReportDefinitionId && l.ReportTagId == r.ReportTagId, ct);
        if (!exists)
        {
            _db.ReportDefinitionTags.Add(new ReportDefinitionTag { Id = Guid.NewGuid(), ReportDefinitionId = r.ReportDefinitionId, ReportTagId = r.ReportTagId });
            await _db.SaveChangesAsync(ct);
        }
    }
}

public class UnassignReportTagCommandHandler : IRequestHandler<UnassignReportTagCommand>
{
    private readonly ApplicationDbContext _db; private readonly IReportAccessService _access;
    public UnassignReportTagCommandHandler(ApplicationDbContext db, IReportAccessService access) { _db = db; _access = access; }
    public async Task Handle(UnassignReportTagCommand r, CancellationToken ct)
    {
        await _access.EnsureCanEditAsync(r.ReportDefinitionId, ct);
        var link = await _db.ReportDefinitionTags.FirstOrDefaultAsync(l => l.ReportDefinitionId == r.ReportDefinitionId && l.ReportTagId == r.ReportTagId, ct);
        if (link is not null) { _db.ReportDefinitionTags.Remove(link); await _db.SaveChangesAsync(ct); }
    }
}
```

- [ ] **Step 2: Add the tags query**

Append to `ReportOrganizationQueries.cs`:

```csharp
public record GetReportTagsQuery : IRequest<List<ReportTagDto>>;

public class GetReportTagsQueryHandler : IRequestHandler<GetReportTagsQuery, List<ReportTagDto>>
{
    private readonly ApplicationDbContext _db; private readonly IMapper _mapper;
    public GetReportTagsQueryHandler(ApplicationDbContext db, IMapper mapper) { _db = db; _mapper = mapper; }
    public async Task<List<ReportTagDto>> Handle(GetReportTagsQuery q, CancellationToken ct)
        => _mapper.Map<List<ReportTagDto>>(await _db.ReportTags.OrderBy(t => t.Name).ToListAsync(ct));
}
```

- [ ] **Step 3: Add tag endpoints to the controller**

```csharp
// Tags
[HttpGet("tags")]
[RequirePermission("Platform.Reports.View")]
public async Task<ActionResult<ApiResponse<List<ReportTagDto>>>> GetTags(CancellationToken ct)
{ var result = await Mediator.Send(new GetReportTagsQuery(), ct); return OkResponse(result); }

[HttpPost("tags")]
[RequirePermission("Platform.Reports.Edit")]
public async Task<ActionResult<ApiResponse<ReportTagDto>>> CreateTag([FromBody] CreateReportTagCommand command, CancellationToken ct)
{ var result = await Mediator.Send(command, ct); return CreatedResponse(result); }

[HttpDelete("tags/{tagId:guid}")]
[RequirePermission("Platform.Reports.Edit")]
public async Task<ActionResult<ApiResponse>> DeleteTag(Guid tagId, CancellationToken ct)
{ await Mediator.Send(new DeleteReportTagCommand(tagId), ct); return OkResponse("Tag deleted"); }

[HttpPost("{id:guid}/tags/{tagId:guid}")]
[RequirePermission("Platform.Reports.Edit")]
public async Task<ActionResult<ApiResponse>> AssignTag(Guid id, Guid tagId, CancellationToken ct)
{ await Mediator.Send(new AssignReportTagCommand(id, tagId), ct); return OkResponse("Tag assigned"); }

[HttpDelete("{id:guid}/tags/{tagId:guid}")]
[RequirePermission("Platform.Reports.Edit")]
public async Task<ActionResult<ApiResponse>> UnassignTag(Guid id, Guid tagId, CancellationToken ct)
{ await Mediator.Send(new UnassignReportTagCommand(id, tagId), ct); return OkResponse("Tag unassigned"); }
```

- [ ] **Step 4: Build**

Run: `dotnet build backend/src/HR.Api/HR.Api.csproj`
Expected: BUILD succeeds.

- [ ] **Step 5: Commit**

```bash
git add backend/src/HR.Modules/Platform/Commands/Reports/ReportTagCommands.cs backend/src/HR.Modules/Platform/Queries/Reports/ReportOrganizationQueries.cs backend/src/HR.Modules/Platform/Controllers/ReportsController.cs
git commit -m "feat(reports): tag CRUD + assign/unassign endpoints"
```

---

## Task D3: Favorites / Pin / Recent + list view-filters + LastViewedAt stamping

**Files:**
- Create: `backend/src/HR.Modules/Platform/Commands/Reports/ReportUserStateCommands.cs`
- Modify: `backend/src/HR.Modules/Platform/Queries/Reports/ReportQueries.cs` — `GetReportsQuery` gains `View`/`FolderId`/`TagId`; `RunReportQueryHandler` stamps `LastViewedAt`.
- Modify: `backend/src/HR.Modules/Platform/Controllers/ReportsController.cs`

**Interfaces:**
- Consumes: `IReportAccessService.EnsureCanReadAsync`, `ApplicationDbContext.ReportUserStates`, `ApplicationDbContext.ReportDefinitionTags`.
- Produces:
  - `record ToggleReportFavoriteCommand(Guid ReportDefinitionId) : IRequest<bool>` (returns new IsFavorite)
  - `record ToggleReportPinCommand(Guid ReportDefinitionId) : IRequest<bool>` (returns new IsPinned)
  - `static Task<ReportUserState> ReportUserStateHelper.GetOrCreateAsync(ApplicationDbContext, Guid userId, Guid reportId, CancellationToken)`
  - `GetReportsQuery` new props: `string? View` (`favorites`|`recent`|`pinned`), `Guid? FolderId`, `Guid? TagId`.

- [ ] **Step 1: Write the failing test (pure toggle semantics)**

Toggle is DB-touching; add a `[SkippableFact]` to a new `backend/tests/HR.Modules.Platform.Tests/Reports/ReportUserStateTests.cs` asserting: first favorite toggle → true (row created), second toggle → false.

```csharp
[SkippableFact]
public async Task Favorite_toggle_flips_and_persists()
{
    Skip.If(string.IsNullOrWhiteSpace(Conn), "Set REPORTS_TEST_DB to run.");
    var tenant = Guid.NewGuid(); var owner = Guid.NewGuid();
    var user = new StubUser(owner, tenant);
    var opts = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(Conn).Options;
    await using var db = new ApplicationDbContext(opts, user);
    await using var tx = await db.Database.BeginTransactionAsync();

    var report = new ReportDefinition { Id = Guid.NewGuid(), TenantId = tenant, Code="U"+Guid.NewGuid().ToString("N")[..6], NameEn="r", NameAr="ر", OwnerId = owner, Scope = ReportScope.Personal, PrimaryObjectId = Guid.NewGuid() };
    db.Set<ReportDefinition>().Add(report); await db.SaveChangesAsync();

    var access = new ReportAccessService(db, user);
    var handler = new ToggleReportFavoriteCommandHandler(db, user, access);

    (await handler.Handle(new ToggleReportFavoriteCommand(report.Id), default)).Should().BeTrue();
    (await handler.Handle(new ToggleReportFavoriteCommand(report.Id), default)).Should().BeFalse();

    await tx.RollbackAsync();
}
```

(Reuse the `StubUser` + `Conn` pattern; if this test file is separate, copy the `StubUser` class and `Conn` accessor from `ReportAccessServiceTests`.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~ReportUserStateTests`
Expected: FAIL — command/handler do not exist (compile error).

- [ ] **Step 3: Write the user-state commands + helper**

```csharp
// ReportUserStateCommands.cs
using System;
using System.Threading;
using System.Threading.Tasks;
using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Reports;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Reports;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Commands.Reports;

public static class ReportUserStateHelper
{
    public static async Task<ReportUserState> GetOrCreateAsync(ApplicationDbContext db, Guid userId, Guid reportId, CancellationToken ct)
    {
        var state = await db.ReportUserStates.FirstOrDefaultAsync(s => s.UserId == userId && s.ReportDefinitionId == reportId, ct);
        if (state is null)
        {
            state = new ReportUserState { Id = Guid.NewGuid(), UserId = userId, ReportDefinitionId = reportId };
            db.ReportUserStates.Add(state);
        }
        return state;
    }
}

public record ToggleReportFavoriteCommand(Guid ReportDefinitionId) : IRequest<bool>;
public record ToggleReportPinCommand(Guid ReportDefinitionId) : IRequest<bool>;

public class ToggleReportFavoriteCommandHandler : IRequestHandler<ToggleReportFavoriteCommand, bool>
{
    private readonly ApplicationDbContext _db; private readonly ICurrentUserService _user; private readonly IReportAccessService _access;
    public ToggleReportFavoriteCommandHandler(ApplicationDbContext db, ICurrentUserService user, IReportAccessService access) { _db = db; _user = user; _access = access; }
    public async Task<bool> Handle(ToggleReportFavoriteCommand r, CancellationToken ct)
    {
        await _access.EnsureCanReadAsync(r.ReportDefinitionId, ct);
        var state = await ReportUserStateHelper.GetOrCreateAsync(_db, _user.UserId, r.ReportDefinitionId, ct);
        state.IsFavorite = !state.IsFavorite;
        await _db.SaveChangesAsync(ct);
        return state.IsFavorite;
    }
}

public class ToggleReportPinCommandHandler : IRequestHandler<ToggleReportPinCommand, bool>
{
    private readonly ApplicationDbContext _db; private readonly ICurrentUserService _user; private readonly IReportAccessService _access;
    public ToggleReportPinCommandHandler(ApplicationDbContext db, ICurrentUserService user, IReportAccessService access) { _db = db; _user = user; _access = access; }
    public async Task<bool> Handle(ToggleReportPinCommand r, CancellationToken ct)
    {
        await _access.EnsureCanReadAsync(r.ReportDefinitionId, ct);
        var state = await ReportUserStateHelper.GetOrCreateAsync(_db, _user.UserId, r.ReportDefinitionId, ct);
        state.IsPinned = !state.IsPinned;
        await _db.SaveChangesAsync(ct);
        return state.IsPinned;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests --filter FullyQualifiedName~ReportUserStateTests`
Expected: locally **Skipped**; PASS with `REPORTS_TEST_DB` set. BUILD must succeed.

- [ ] **Step 5: Stamp `LastViewedAt` on run**

In `RunReportQueryHandler.Handle` (edited in A3), after the access check and before returning, upsert the user state's `LastViewedAt`. Inject `ICurrentUserService _user` and `ApplicationDbContext _db` into the handler:

```csharp
public async Task<ReportResult> Handle(RunReportQuery request, CancellationToken ct)
{
    await _access.EnsureCanReadAsync(request.Id, ct);
    var result = await _exec.RunAsync(request.Id, request.Page, request.PageSize, ct);
    var state = await ReportUserStateHelper.GetOrCreateAsync(_db, _user.UserId, request.Id, ct);
    state.LastViewedAt = DateTime.UtcNow;
    await _db.SaveChangesAsync(ct);
    return result;
}
```

Add the corresponding fields/constructor params (`ApplicationDbContext _db`, `ICurrentUserService _user`) and `using HR.Modules.Platform.Commands.Reports;` to `ReportQueries.cs`.

- [ ] **Step 6: Add view-filters to `GetReportsQuery`**

Extend the record and handler in `ReportQueries.cs`:

```csharp
public record GetReportsQuery : IRequest<PaginatedList<ReportDefinitionDto>>
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 10;
    public string? Search { get; init; }
    public string? Scope { get; init; }
    public string? View { get; init; }      // favorites | recent | pinned
    public Guid? FolderId { get; init; }
    public Guid? TagId { get; init; }
}
```

In `GetReportsQueryHandler.Handle`, after the visibility filter + search, apply the organization filters (inject `ICurrentUserService _user`; it is already available if added — otherwise add it):

```csharp
if (request.FolderId is { } fid)
    query = query.Where(r => r.FolderId == fid);

if (request.TagId is { } tid)
    query = query.Where(r => _context.ReportDefinitionTags.Any(l => l.ReportTagId == tid && l.ReportDefinitionId == r.Id));

var uid = _user.UserId;
if (string.Equals(request.View, "favorites", StringComparison.OrdinalIgnoreCase))
    query = query.Where(r => _context.ReportUserStates.Any(s => s.UserId == uid && s.ReportDefinitionId == r.Id && s.IsFavorite));
else if (string.Equals(request.View, "pinned", StringComparison.OrdinalIgnoreCase))
    query = query.Where(r => _context.ReportUserStates.Any(s => s.UserId == uid && s.ReportDefinitionId == r.Id && s.IsPinned));

if (string.Equals(request.View, "recent", StringComparison.OrdinalIgnoreCase))
{
    // Recent = reports the caller has viewed, most-recent first.
    var recent = from r in query
                 join s in _context.ReportUserStates.Where(s => s.UserId == uid && s.LastViewedAt != null)
                     on r.Id equals s.ReportDefinitionId
                 orderby s.LastViewedAt descending
                 select r;
    query = recent;
}
```

For the non-recent branches, keep the existing `OrderByDescending(r => r.CreatedAt)`. For the recent branch the ordering is already applied — guard the final `OrderByDescending` so it does not override recent (e.g. only apply `OrderByDescending(CreatedAt)` when `View != "recent"`). Add `using System;` if needed.

- [ ] **Step 7: Add favorite/pin endpoints**

```csharp
[HttpPost("{id:guid}/favorite")]
[RequirePermission("Platform.Reports.View")]
public async Task<ActionResult<ApiResponse<bool>>> ToggleFavorite(Guid id, CancellationToken ct)
{ var result = await Mediator.Send(new ToggleReportFavoriteCommand(id), ct); return OkResponse(result); }

[HttpPost("{id:guid}/pin")]
[RequirePermission("Platform.Reports.View")]
public async Task<ActionResult<ApiResponse<bool>>> TogglePin(Guid id, CancellationToken ct)
{ var result = await Mediator.Send(new ToggleReportPinCommand(id), ct); return OkResponse(result); }
```

- [ ] **Step 8: Build + full test run**

Run: `dotnet test backend/tests/HR.Modules.Platform.Tests`
Expected: BUILD succeeds; all non-skippable tests PASS; DB-touching tests Skipped locally.

- [ ] **Step 9: Commit**

```bash
git add backend/src/HR.Modules/Platform/Commands/Reports/ReportUserStateCommands.cs backend/src/HR.Modules/Platform/Queries/Reports/ReportQueries.cs backend/src/HR.Modules/Platform/Controllers/ReportsController.cs backend/tests/HR.Modules.Platform.Tests/Reports/ReportUserStateTests.cs
git commit -m "feat(reports): favorites/pin/recent state + list view-filters + LastViewedAt stamping"
```

---

## Final verification

- [ ] **Full suite:** `dotnet test backend/tests/HR.Modules.Platform.Tests` → all green (DB-touching skipped locally).
- [ ] **API builds:** `dotnet build backend/src/HR.Api/HR.Api.csproj` → success.
- [ ] **Optional live integration:** set `REPORTS_TEST_DB` to a throwaway Postgres and re-run to exercise the `[SkippableFact]` access/share/state tests end-to-end.
- [ ] **Deploy (executing session):** apply the `ReportOrganization` migration to Azure Postgres (Key Vault `secretpulse/hrcloud-db-password`), then zip-deploy `HR.Api` per the CLAUDE.md gotcha (build the zip via `System.IO.Compression.ZipFile` with `.Replace('\\','/')`). Verify `POST /api/platform/reports/{id}/run` still 401s unauthenticated and the new `{id}/shares`, `folders`, `tags`, `{id}/favorite`, `{id}/pin` routes appear in Swagger.
- [ ] **Update memory:** append Phase 2 completion to `reports-engine-r1.md`.

---

## Self-Review notes (author)

- **Spec §6.1 ownership/visibility** → Tasks A1–A3 (resolver wired to list/get/run).
- **Spec §6.1 share-management endpoints** → Tasks B1–B2.
- **Spec §6.2 folders** → C1/C2/D1. **tags** → C1/C2/D2. **favorites/pinned** → C1/D3. **recent** → C1/D3 (LastViewedAt stamped on run, `view=recent`).
- **Spec §7 API surface**: `{id}/shares` ✓, `folders` ✓, `tags`+`{id}/tags` ✓, `{id}/favorite`+`{id}/pin` ✓, list `view=/folderId=/tagId=` ✓. `{id}/export` is **R1 Phase 3** (frontend), not this plan — out of scope here.
- **Known limits preserved:** `ReportScope.Department`/`Shared` honored only via explicit shares (no `ReportDefinition.DepartmentId` column added) — matches the Phase 1 review note.
- **Permissions:** no new strings; reuse `Platform.Reports.View`/`Edit` (already seeded). No permission migration needed.
