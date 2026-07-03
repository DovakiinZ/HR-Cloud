using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance.StateMachine;
using HR.Domain.Engines.MasterData;
using HR.Modules.Employees.Entities;
using HR.Domain.Enums;
using HR.Infrastructure.Engines.Finance;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class PayrollTransactionServiceTests
{
    private static readonly Guid Tenant = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class FakeUser : ICurrentUserService
    {
        public Guid UserId => Guid.Parse("22222222-2222-2222-2222-222222222222");
        public Guid TenantId => Tenant;
        public string? Email => "t@t.local";
        public IReadOnlyList<string> Permissions { get; } = new[] { "Payroll.Create" };
        public bool IsAuthenticated => true;
    }

    private static ApplicationDbContext Ctx(string name) => new(
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(name).Options, new FakeUser());

    private static PayrollTransactionService Svc(ApplicationDbContext db) => new(db, new FakeUser(), new PayrollPeriodGuard(db));

    // Seeds one employee + one DeductionType master-data item, returns their ids.
    private static async Task<(Guid empId, Guid typeId)> SeedAsync(ApplicationDbContext db, string objectType)
    {
        var emp = new Employee { EmployeeNumber = "E1", FirstName = "Ali", LastName = "Saud", Email = "ali@test.local" };
        db.Employees.Add(emp);
        var type = new MasterDataItem { ObjectType = objectType, Code = "PENALTY", NameAr = "جزاء", NameEn = "Penalty" };
        db.MasterDataItems.Add(type);
        await db.SaveChangesAsync();
        return (emp.Id, type.Id);
    }

    private static CreatePayrollTransactionArgs DeductionArgs(Guid empId, Guid typeId, decimal amount = 100m,
        DateTime? effective = null, bool submit = false) =>
        new(PayrollTransactionKind.Deduction, empId, typeId, amount,
            effective ?? new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc),
            null, false, null, "test", null, submit);

    [Fact]
    public async Task Create_starts_in_draft_and_derives_target_period()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var (empId, typeId) = await SeedAsync(db, MasterDataObjectType.DeductionType);
        var id = await Svc(db).CreateAsync(DeductionArgs(empId, typeId), default);

        var row = await db.PayrollTransactions.SingleAsync(x => x.Id == id);
        row.Status.Should().Be(PayrollTransactionStatus.Draft);
        row.TargetPeriodYear.Should().Be(2026);
        row.TargetPeriodMonth.Should().Be(7);
        row.TransactionDate.Should().Be(row.EffectiveDate); // defaulted
    }

    [Fact]
    public async Task Create_with_submit_goes_to_pending_approval()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var (empId, typeId) = await SeedAsync(db, MasterDataObjectType.DeductionType);
        var id = await Svc(db).CreateAsync(DeductionArgs(empId, typeId, submit: true), default);
        (await db.PayrollTransactions.SingleAsync(x => x.Id == id)).Status
            .Should().Be(PayrollTransactionStatus.PendingApproval);
    }

    [Fact]
    public async Task Create_rejects_negative_amount()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var (empId, typeId) = await SeedAsync(db, MasterDataObjectType.DeductionType);
        var act = () => Svc(db).CreateAsync(DeductionArgs(empId, typeId, amount: -5m), default);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Create_rejects_type_whose_object_type_mismatches_kind()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        // Seed an AdditionType but create a Deduction → mismatch.
        var (empId, typeId) = await SeedAsync(db, MasterDataObjectType.AdditionType);
        var act = () => Svc(db).CreateAsync(DeductionArgs(empId, typeId), default);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Create_rejects_inactive_type()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        // Seed an employee and a DeductionType master-data item that is inactive.
        var emp = new Employee { EmployeeNumber = "E2", FirstName = "Sara", LastName = "Ali", Email = "sara@test.local" };
        db.Employees.Add(emp);
        var type = new MasterDataItem
        {
            ObjectType = MasterDataObjectType.DeductionType,
            Code = "INACTIVE_DED",
            NameAr = "خصم معطل",
            NameEn = "Inactive Deduction",
            IsActive = false
        };
        db.MasterDataItems.Add(type);
        await db.SaveChangesAsync();

        var act = () => Svc(db).CreateAsync(DeductionArgs(emp.Id, type.Id), default);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*{type.Id}*inactive*");
    }

    [Fact]
    public async Task Update_is_rejected_once_not_draft()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var (empId, typeId) = await SeedAsync(db, MasterDataObjectType.DeductionType);
        var svc = Svc(db);
        var id = await svc.CreateAsync(DeductionArgs(empId, typeId), default);
        await svc.SubmitAsync(id, default);
        var act = () => svc.UpdateAsync(id, new UpdatePayrollTransactionArgs(typeId, 200m,
            new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc), null, false, null, null, null), default);
        await act.Should().ThrowAsync<InvalidPayrollTransactionStateException>();
    }

    [Fact]
    public async Task Submit_then_approve_advances_status()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var (empId, typeId) = await SeedAsync(db, MasterDataObjectType.DeductionType);
        var svc = Svc(db);
        var id = await svc.CreateAsync(DeductionArgs(empId, typeId), default);
        await svc.SubmitAsync(id, default);
        await svc.ApproveAsync(id, default);
        (await db.PayrollTransactions.SingleAsync(x => x.Id == id)).Status
            .Should().Be(PayrollTransactionStatus.Approved);
    }

    [Fact]
    public async Task Approve_from_draft_is_illegal()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var (empId, typeId) = await SeedAsync(db, MasterDataObjectType.DeductionType);
        var svc = Svc(db);
        var id = await svc.CreateAsync(DeductionArgs(empId, typeId), default);
        var act = () => svc.ApproveAsync(id, default);
        await act.Should().ThrowAsync<InvalidPayrollTransactionStateException>();
    }

    [Fact]
    public async Task Reject_sets_reason_and_status()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var (empId, typeId) = await SeedAsync(db, MasterDataObjectType.DeductionType);
        var svc = Svc(db);
        var id = await svc.CreateAsync(DeductionArgs(empId, typeId, submit: true), default);
        await svc.RejectAsync(id, "missing approval doc", default);
        var row = await db.PayrollTransactions.SingleAsync(x => x.Id == id);
        row.Status.Should().Be(PayrollTransactionStatus.Rejected);
        row.StatusReason.Should().Be("missing approval doc");
    }

    [Fact]
    public async Task List_filters_by_kind_and_resolves_names()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var (empId, typeId) = await SeedAsync(db, MasterDataObjectType.DeductionType);
        var svc = Svc(db);
        await svc.CreateAsync(DeductionArgs(empId, typeId), default);

        var rows = await svc.ListAsync(new PayrollTransactionFilter(
            PayrollTransactionKind.Deduction, null, null, null, null, null, null, null), default);
        rows.Should().HaveCount(1);
        rows[0].EmployeeName.Should().Contain("Ali");
        rows[0].TypeName.Should().Be("جزاء");

        var none = await svc.ListAsync(new PayrollTransactionFilter(
            PayrollTransactionKind.Addition, null, null, null, null, null, null, null), default);
        none.Should().BeEmpty();
    }

    // Postgres 'timestamp with time zone' rejects DateTime with Kind != Utc. Dates arriving from JSON
    // ("2026-07-05") deserialize as Kind=Unspecified, so the service must normalize them to UTC before save.
    [Fact]
    public async Task Create_normalizes_supplied_dates_to_utc()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var (empId, typeId) = await SeedAsync(db, MasterDataObjectType.DeductionType);
        var unspecified = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Unspecified);
        var args = new CreatePayrollTransactionArgs(PayrollTransactionKind.Deduction, empId, typeId, 100m,
            unspecified, unspecified, true, unspecified, "t", null, false);

        var id = await Svc(db).CreateAsync(args, default);

        var row = await db.PayrollTransactions.SingleAsync(x => x.Id == id);
        row.EffectiveDate.Kind.Should().Be(DateTimeKind.Utc);
        row.TransactionDate.Kind.Should().Be(DateTimeKind.Utc);
        row.RecurrenceEndDate!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task Update_normalizes_supplied_dates_to_utc()
    {
        await using var db = Ctx($"t-{Guid.NewGuid()}");
        var (empId, typeId) = await SeedAsync(db, MasterDataObjectType.DeductionType);
        var svc = Svc(db);
        var id = await svc.CreateAsync(DeductionArgs(empId, typeId), default);
        var unspecified = new DateTime(2026, 8, 9, 0, 0, 0, DateTimeKind.Unspecified);

        await svc.UpdateAsync(id, new UpdatePayrollTransactionArgs(typeId, 120m,
            unspecified, unspecified, true, unspecified, null, null), default);

        var row = await db.PayrollTransactions.SingleAsync(x => x.Id == id);
        row.EffectiveDate.Kind.Should().Be(DateTimeKind.Utc);
        row.TransactionDate.Kind.Should().Be(DateTimeKind.Utc);
        row.RecurrenceEndDate!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }
}
