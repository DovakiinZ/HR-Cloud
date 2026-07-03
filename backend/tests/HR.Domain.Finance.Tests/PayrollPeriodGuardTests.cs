using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Enums;
using HR.Infrastructure.Engines.Finance;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>TDD tests for Task 4 (SP3): PayrollPeriodGuard — finds immutable runs that cover a given
/// (employee, effectiveDate) and throws PayrollPeriodClosedException; allows access when no immutable
/// run covers that period.</summary>
public class PayrollPeriodGuardTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId    => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid TenantId  => Tenant;
        public string? Email  => "t@t.local";
        public IReadOnlyList<string> Permissions { get; } = Array.Empty<string>();
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext Ctx(string name) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options,
        new FakeUser());

    // ── seed helpers ──────────────────────────────────────────────────────────────

    /// <summary>Seeds a published monthly definition version (CutoffDay 27, CarryToNextPeriod false),
    /// an employee, a run for (year,month) in the requested state, and one included population row.
    /// Returns (definitionId, employeeId, runId).</summary>
    private static async Task<(Guid defId, Guid empId, Guid runId)> SeedRunAsync(
        ApplicationDbContext db, int year, int month, PayrollRunState state)
    {
        // Definition
        var def = new HR.Domain.Engines.Finance.Entities.PayrollDefinition
        {
            TenantId   = Tenant,
            Code       = $"MONTHLY-{Guid.NewGuid():N}",
            Name       = "Monthly",
            Status     = PayrollDefinitionStatus.Active,
        };
        db.PayrollDefinitions.Add(def);

        // Definition version (published)
        var ver = new PayrollDefinitionVersion
        {
            TenantId              = Tenant,
            PayrollDefinitionId   = def.Id,
            VersionNumber         = 1,
            Status                = VersionStatus.Published,
            Frequency             = PayFrequency.Monthly,
            CutoffDay             = 27,
            CarryToNextPeriod     = false,   // effectiveDate.Day ≤ 27 → stays in same month
            PublishedAt           = DateTime.UtcNow,
        };
        db.PayrollDefinitionVersions.Add(ver);
        def.CurrentVersionId = ver.Id;

        // Employee
        var emp = new Employee
        {
            TenantId       = Tenant,
            EmployeeNumber = $"E-{Guid.NewGuid():N}",
            FirstName      = "Ali",
            LastName       = "Saud",
            Email          = $"ali-{Guid.NewGuid():N}@test.local",
            HireDate       = new DateTime(2024, 1, 1),
            DateOfBirth    = new DateTime(1990, 1, 1),
        };
        db.Employees.Add(emp);

        // Run
        var run = new PayrollRun
        {
            TenantId                   = Tenant,
            RunNumber                  = $"RUN-{year}-{month:D2}",
            PayrollDefinitionId        = def.Id,
            PayrollDefinitionVersionId = ver.Id,
            TargetPeriodYear           = year,
            TargetPeriodMonth          = month,
            PeriodStart                = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd                  = new DateTime(year, month, 27, 0, 0, 0, DateTimeKind.Utc),
            State                      = state,
        };
        db.PayrollRuns.Add(run);

        // Population row — employee is included in this run
        var pop = new PayrollRunPopulation
        {
            TenantId       = Tenant,
            PayrollRunId   = run.Id,
            EmployeeId     = emp.Id,
            EmployeeNumber = emp.EmployeeNumber,
            EmployeeName   = $"{emp.FirstName} {emp.LastName}",
            IsIncluded     = true,
        };
        db.PayrollRunPopulations.Add(pop);

        await db.SaveChangesAsync();
        return (def.Id, emp.Id, run.Id);
    }

    // ── tests ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Blocks_when_an_immutable_run_covers_the_period()
    {
        await using var db = Ctx($"guard-block-{Guid.NewGuid()}");
        var (_, empId, _) = await SeedRunAsync(db, 2026, 7, PayrollRunState.Approved); // Approved ⇒ immutable

        var guard     = new PayrollPeriodGuard(db);
        var effective = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc); // day 15 ≤ cutoff 27 → July

        Func<Task> act = async () => await guard.EnsurePeriodOpenForAsync(empId, effective, default);

        (await act.Should().ThrowAsync<PayrollPeriodClosedException>())
            .Which.Payload.BlockingRunState.Should().Be("Approved");
    }

    [Fact]
    public async Task Allows_when_run_is_still_mutable()
    {
        await using var db = Ctx($"guard-allow-{Guid.NewGuid()}");
        var (_, empId, _) = await SeedRunAsync(db, 2026, 7, PayrollRunState.Draft); // Draft ⇒ mutable

        var guard = new PayrollPeriodGuard(db);
        Func<Task> act = async () => await guard.EnsurePeriodOpenForAsync(empId,
            new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Allows_when_employee_is_not_in_the_immutable_run_population()
    {
        await using var db = Ctx($"guard-different-emp-{Guid.NewGuid()}");
        // Seed an immutable run for a specific employee
        await SeedRunAsync(db, 2026, 7, PayrollRunState.Approved);

        // A completely different, unseeded employee
        var otherEmpId = Guid.NewGuid();

        var guard = new PayrollPeriodGuard(db);
        Func<Task> act = async () => await guard.EnsurePeriodOpenForAsync(otherEmpId,
            new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Allows_when_effective_date_is_outside_the_immutable_run_period()
    {
        await using var db = Ctx($"guard-different-period-{Guid.NewGuid()}");
        // Immutable run covers July 2026
        var (_, empId, _) = await SeedRunAsync(db, 2026, 7, PayrollRunState.Approved);

        var guard = new PayrollPeriodGuard(db);
        // August 2026 effective date — different period
        Func<Task> act = async () => await guard.EnsurePeriodOpenForAsync(empId,
            new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc), default);

        await act.Should().NotThrowAsync();
    }
}
