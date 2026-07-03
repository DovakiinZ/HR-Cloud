using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Finance;
using HR.Domain.Enums;
using HR.Infrastructure.Engines.Finance;
using HR.Infrastructure.Persistence;
using HR.Modules.Employees.Entities;
using HR.Domain.Engines.MasterData;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class PayrollTransactionOriginTests
{
    private static readonly Guid Tenant = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId => Guid.Parse("44444444-4444-4444-4444-444444444444");
        public Guid TenantId => Tenant;
        public string? Email => "origin@t.local";
        public IReadOnlyList<string> Permissions { get; } = new[] { "Payroll.Create" };
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext Ctx(string name) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options, new FakeUser());

    private static PayrollTransactionService Svc(ApplicationDbContext db) =>
        new(db, new FakeUser(), new PayrollPeriodGuard(db));

    private static async Task<(Guid empId, Guid typeId)> SeedAsync(ApplicationDbContext db)
    {
        var emp = new Employee { EmployeeNumber = "EO1", FirstName = "Nora", LastName = "Hassan", Email = "nora@test.local" };
        db.Employees.Add(emp);
        var type = new MasterDataItem
        {
            ObjectType = MasterDataObjectType.DeductionType,
            Code = "MANUAL",
            NameAr = "يدوي",
            NameEn = "Manual"
        };
        db.MasterDataItems.Add(type);
        await db.SaveChangesAsync();
        return (emp.Id, type.Id);
    }

    [Fact]
    public async Task Create_defaults_origin_to_system()
    {
        await using var ctx = Ctx($"origin-{Guid.NewGuid()}");
        var svc = Svc(ctx);
        var (emp, typeId) = await SeedAsync(ctx);

        var id = await svc.CreateAsync(new CreatePayrollTransactionArgs(
            PayrollTransactionKind.Deduction, emp, typeId, 10m,
            new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
            null, false, null, null, null, SubmitImmediately: false), default);

        var txn = await ctx.PayrollTransactions.FindAsync(id);
        txn!.Origin.Should().Be(PayrollTransactionOrigin.System);
        txn.CreatedFromRunId.Should().BeNull();
    }

    [Fact]
    public async Task Create_stamps_explicit_origin_and_createdFromRunId()
    {
        await using var ctx = Ctx($"origin-explicit-{Guid.NewGuid()}");
        var svc = Svc(ctx);
        var (emp, typeId) = await SeedAsync(ctx);
        var runId = Guid.NewGuid();

        var id = await svc.CreateAsync(new CreatePayrollTransactionArgs(
            PayrollTransactionKind.Deduction, emp, typeId, 20m,
            new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
            null, false, null, null, null, SubmitImmediately: false,
            Origin: PayrollTransactionOrigin.RunPage,
            CreatedFromRunId: runId), default);

        var txn = await ctx.PayrollTransactions.FindAsync(id);
        txn!.Origin.Should().Be(PayrollTransactionOrigin.RunPage);
        txn.CreatedFromRunId.Should().Be(runId);
    }

    [Fact]
    public async Task Dto_exposes_origin_and_createdFromRunId()
    {
        await using var ctx = Ctx($"origin-dto-{Guid.NewGuid()}");
        var svc = Svc(ctx);
        var (emp, typeId) = await SeedAsync(ctx);
        var runId = Guid.NewGuid();

        var id = await svc.CreateAsync(new CreatePayrollTransactionArgs(
            PayrollTransactionKind.Deduction, emp, typeId, 30m,
            new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
            null, false, null, null, null, SubmitImmediately: false,
            Origin: PayrollTransactionOrigin.DeductionsPage,
            CreatedFromRunId: runId), default);

        var dto = await svc.GetAsync(id, default);
        dto.Should().NotBeNull();
        dto!.Origin.Should().Be(PayrollTransactionOrigin.DeductionsPage);
        dto.CreatedFromRunId.Should().Be(runId);
    }
}
