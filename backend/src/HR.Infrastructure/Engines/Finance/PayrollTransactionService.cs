using HR.Application.Common.Interfaces;
using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.Finance.StateMachine;
using HR.Domain.Engines.MasterData;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Engines.Finance;

public sealed class PayrollTransactionService : IPayrollTransactionService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IPayrollPeriodGuard _guard;

    public PayrollTransactionService(ApplicationDbContext db, ICurrentUserService user, IPayrollPeriodGuard guard)
    {
        _db = db;
        _user = user;
        _guard = guard;
    }

    // Postgres 'timestamp with time zone' columns only accept UTC DateTimes. Dates supplied from the API
    // deserialize as Kind=Unspecified, so normalize before persisting (mirrors EndOfServiceEngine.AsUtc).
    private static DateTime AsUtc(DateTime d) =>
        d.Kind == DateTimeKind.Utc ? d : DateTime.SpecifyKind(d, DateTimeKind.Utc);

    private static DateTime? AsUtc(DateTime? d) => d is { } v ? AsUtc(v) : null;

    public async Task<Guid> CreateAsync(CreatePayrollTransactionArgs args, CancellationToken ct)
    {
        if (args.Amount < 0) throw new InvalidOperationException("Amount must be non-negative.");
        await EnsureEmployeeAsync(args.EmployeeId, ct);
        await EnsureTypeMatchesKindAsync(args.TypeId, args.Kind, ct);

        var effective = AsUtc(args.EffectiveDate);
        var txn = new PayrollTransaction
        {
            Kind = args.Kind,
            EmployeeId = args.EmployeeId,
            TypeId = args.TypeId,
            Amount = args.Amount,
            EffectiveDate = effective,
            TransactionDate = AsUtc(args.TransactionDate ?? args.EffectiveDate),
            TargetPeriodYear = effective.Year,
            TargetPeriodMonth = effective.Month,
            IsRecurring = args.IsRecurring,
            RecurrenceEndDate = AsUtc(args.RecurrenceEndDate),
            Notes = args.Notes,
            AttachmentFileId = args.AttachmentFileId,
            SourceModule = "Manual",
            Status = args.SubmitImmediately ? PayrollTransactionStatus.PendingApproval : PayrollTransactionStatus.Draft,
            Origin = args.Origin,
            CreatedFromRunId = args.CreatedFromRunId,
        };
        _db.PayrollTransactions.Add(txn);
        await _db.SaveChangesAsync(ct);
        return txn.Id;
    }

    public async Task UpdateAsync(Guid id, UpdatePayrollTransactionArgs args, CancellationToken ct)
    {
        if (args.Amount < 0) throw new InvalidOperationException("Amount must be non-negative.");
        var txn = await GetTrackedAsync(id, ct);
        if (!PayrollTransactionStateMachine.IsEditable(txn.Status))
            throw new InvalidPayrollTransactionStateException(txn.Status, PayrollTransactionStatus.Draft);
        await EnsureTypeMatchesKindAsync(args.TypeId, txn.Kind, ct);

        txn.TypeId = args.TypeId;
        txn.Amount = args.Amount;
        txn.EffectiveDate = AsUtc(args.EffectiveDate);
        txn.TransactionDate = AsUtc(args.TransactionDate ?? args.EffectiveDate);
        txn.TargetPeriodYear = args.EffectiveDate.Year;
        txn.TargetPeriodMonth = args.EffectiveDate.Month;
        txn.IsRecurring = args.IsRecurring;
        txn.RecurrenceEndDate = AsUtc(args.RecurrenceEndDate);
        txn.Notes = args.Notes;
        txn.AttachmentFileId = args.AttachmentFileId;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SubmitAsync(Guid id, CancellationToken ct) =>
        await TransitionAsync(id, PayrollTransactionStatus.PendingApproval, null, ct);

    public async Task ApproveAsync(Guid id, CancellationToken ct) =>
        await TransitionAsync(id, PayrollTransactionStatus.Approved, null, ct);

    public async Task RejectAsync(Guid id, string reason, CancellationToken ct) =>
        await TransitionAsync(id, PayrollTransactionStatus.Rejected, reason, ct);

    public async Task CancelAsync(Guid id, string? reason, CancellationToken ct) =>
        await TransitionAsync(id, PayrollTransactionStatus.Cancelled, reason, ct);

    public async Task SetAttachmentAsync(Guid id, Guid fileId, CancellationToken ct)
    {
        var txn = await GetTrackedAsync(id, ct);
        if (PayrollTransactionStateMachine.IsImmutable(txn.Status))
            throw new InvalidPayrollTransactionStateException(txn.Status, PayrollTransactionStatus.Draft);
        txn.AttachmentFileId = fileId;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var txn = await GetTrackedAsync(id, ct);
        if (txn.Status != PayrollTransactionStatus.Draft)
            throw new InvalidPayrollTransactionStateException(txn.Status, PayrollTransactionStatus.Draft);
        txn.IsDeleted = true;
        txn.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<PayrollTransactionDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var rows = await Query().Where(x => x.txn.Id == id).ToListAsync(ct);
        return rows.Select(Project).FirstOrDefault();
    }

    public async Task<IReadOnlyList<PayrollTransactionDto>> ListAsync(PayrollTransactionFilter f, CancellationToken ct)
    {
        var q = Query();
        if (f.Kind is not null) q = q.Where(x => x.txn.Kind == f.Kind);
        if (f.EmployeeId is not null) q = q.Where(x => x.txn.EmployeeId == f.EmployeeId);
        if (f.PeriodYear is not null) q = q.Where(x => x.txn.TargetPeriodYear == f.PeriodYear);
        if (f.PeriodMonth is not null) q = q.Where(x => x.txn.TargetPeriodMonth == f.PeriodMonth);
        if (f.TypeId is not null) q = q.Where(x => x.txn.TypeId == f.TypeId);
        if (f.Status is not null) q = q.Where(x => x.txn.Status == f.Status);
        if (f.DateFrom is not null) q = q.Where(x => x.txn.EffectiveDate >= f.DateFrom);
        if (f.DateTo is not null) q = q.Where(x => x.txn.EffectiveDate <= f.DateTo);

        var rows = await q.OrderByDescending(x => x.txn.CreatedAt).ToListAsync(ct);
        return rows.Select(Project).ToList();
    }

    // --- helpers ---

    private async Task TransitionAsync(Guid id, PayrollTransactionStatus to, string? reason, CancellationToken ct)
    {
        var txn = await GetTrackedAsync(id, ct);
        PayrollTransactionStateMachine.EnsureCanTransition(txn.Status, to);
        if (to == PayrollTransactionStatus.Approved)
            await _guard.EnsurePeriodOpenForAsync(txn.EmployeeId, txn.EffectiveDate, ct);
        txn.Status = to;
        if (reason is not null) txn.StatusReason = reason;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<PayrollTransaction> GetTrackedAsync(Guid id, CancellationToken ct) =>
        await _db.PayrollTransactions.FirstOrDefaultAsync(x => x.Id == id, ct)
        ?? throw new InvalidOperationException($"Payroll transaction {id} not found.");

    private async Task EnsureEmployeeAsync(Guid employeeId, CancellationToken ct)
    {
        var exists = await _db.Employees.AnyAsync(e => e.Id == employeeId, ct);
        if (!exists) throw new InvalidOperationException($"Employee {employeeId} not found.");
    }

    private async Task EnsureTypeMatchesKindAsync(Guid typeId, PayrollTransactionKind kind, CancellationToken ct)
    {
        var expected = kind == PayrollTransactionKind.Addition
            ? MasterDataObjectType.AdditionType : MasterDataObjectType.DeductionType;
        var item = await _db.MasterDataItems.FirstOrDefaultAsync(x => x.Id == typeId, ct)
            ?? throw new InvalidOperationException($"Type {typeId} not found.");
        if (!item.IsActive)
            throw new InvalidOperationException($"Type {typeId} is inactive.");
        if (!string.Equals(item.ObjectType, expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Type {typeId} is '{item.ObjectType}', expected '{expected}'.");
    }

    // Join transaction → employee + type so the DTO carries display names.
    private IQueryable<Row> Query() =>
        from txn in _db.PayrollTransactions
        join emp in _db.Employees on txn.EmployeeId equals emp.Id into empJ
        from emp in empJ.DefaultIfEmpty()
        join type in _db.MasterDataItems on txn.TypeId equals type.Id into typeJ
        from type in typeJ.DefaultIfEmpty()
        select new Row { txn = txn, emp = emp, type = type };

    private sealed class Row
    {
        public PayrollTransaction txn = null!;
        public HR.Modules.Employees.Entities.Employee? emp;
        public MasterDataItem? type;
    }

    private static PayrollTransactionDto Project(Row r) => new(
        r.txn.Id, r.txn.Kind, r.txn.EmployeeId,
        EmployeeName(r.emp), r.emp?.EmployeeNumber ?? "",
        r.txn.TypeId, r.type?.NameAr ?? r.type?.NameEn ?? "",
        r.txn.Amount, r.txn.TransactionDate, r.txn.EffectiveDate,
        r.txn.TargetPeriodYear, r.txn.TargetPeriodMonth, r.txn.IsRecurring, r.txn.RecurrenceEndDate,
        r.txn.Notes, r.txn.AttachmentFileId, r.txn.SourceModule, r.txn.ReferenceType, r.txn.ReferenceId,
        r.txn.Status, r.txn.StatusReason, r.txn.PayrollRunId, r.txn.PostedAt,
        r.txn.ReversesTransactionId, r.txn.CreatedAt,
        r.txn.Origin, r.txn.CreatedFromRunId);

    private static string EmployeeName(HR.Modules.Employees.Entities.Employee? e)
    {
        if (e is null) return "";
        var first = string.IsNullOrWhiteSpace(e.FirstNameAr) ? e.FirstName : e.FirstNameAr!;
        var last = string.IsNullOrWhiteSpace(e.LastNameAr) ? e.LastName : e.LastNameAr!;
        return $"{first} {last}".Trim();
    }
}
