using HR.Application.Common.Exceptions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.Finance.StateMachine;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Infrastructure.Engines.Finance;

/// <summary>Implements void / amend / reissue on ADR PAY-4. Void reverses the run's ledger postings (append
/// counter-entries — originals untouched) and flips its consumed transactions to Reversed. Amend voids the
/// old run then creates a linked fresh run; the ledger nets −old + new to the correction. Reissue reuses
/// the payslip engine.</summary>
public sealed class PayrollRunAmendmentService : IPayrollRunAmendmentService
{
    private readonly ApplicationDbContext _db;
    private readonly IFinancialLedger _ledger;
    private readonly ICurrentUserService _user;
    private readonly IAuditLogService _audit;
    private readonly IPayrollRunEngine _runEngine;
    private readonly IPayslipDocumentService _payslips;

    public PayrollRunAmendmentService(
        ApplicationDbContext db, IFinancialLedger ledger, ICurrentUserService user,
        IAuditLogService audit, IPayrollRunEngine runEngine, IPayslipDocumentService payslips)
    {
        _db = db; _ledger = ledger; _user = user; _audit = audit; _runEngine = runEngine; _payslips = payslips;
    }

    private Guid? Actor => _user.IsAuthenticated ? _user.UserId : null;

    public async Task VoidAsync(Guid runId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("سبب الإلغاء مطلوب.", "VOID_REASON_REQUIRED");

        var run = await _db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new NotFoundException("PayrollRun", runId);

        // Only a posted run (Completed/Locked) can be voided — throws 409 otherwise.
        PayrollRunStateMachine.EnsureCanTransition(run.State, PayrollRunState.Voided);

        // 1) Reverse every original ledger posting for this run (counter-entries; never mutate originals).
        var entries = await _ledger.QueryAsync(new LedgerQuery { PayrollRunId = runId }, ct);
        foreach (var e in entries.Where(e => e.Status == LedgerEntryStatus.Posted))
            await _ledger.ReverseAsync(e.Id, reason, ct);

        // 2) Flip consumed transactions to Reversed (their ledger entries were reversed in step 1).
        var txns = await _db.PayrollTransactions
            .Where(t => t.PayrollRunId == runId && t.Status == PayrollTransactionStatus.Posted)
            .ToListAsync(ct);
        foreach (var t in txns)
        {
            PayrollTransactionStateMachine.EnsureCanTransition(t.Status, PayrollTransactionStatus.Reversed);
            t.Status = PayrollTransactionStatus.Reversed;
            t.ReversalReason = reason;
        }

        // 3) Transition the run to Voided + record it.
        var from = run.State;
        var now = DateTime.UtcNow;
        run.State = PayrollRunState.Voided;
        run.VoidedByUserId = Actor;
        run.VoidedAt = now;
        run.VoidReason = reason;
        _db.PayrollRunTransitions.Add(new PayrollRunTransition
        {
            PayrollRunId = runId, FromState = from, ToState = PayrollRunState.Voided,
            At = now, ActorUserId = Actor, Reason = reason,
        });
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("PayrollRunVoided", nameof(PayrollRun), runId,
            new { from = from.ToString(), ledgerEntriesReversed = entries.Count(e => e.Status == LedgerEntryStatus.Posted), transactionsReversed = txns.Count },
            new { reason }, ct);
    }

    public async Task<AmendResult> AmendAsync(Guid runId, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("سبب التعديل مطلوب.", "AMEND_REASON_REQUIRED");

        var old = await _db.PayrollRuns.FirstOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new NotFoundException("PayrollRun", runId);

        // Void the old run first — this both undoes its ledger and releases its hold on the period.
        await VoidAsync(runId, $"تم التجاوز بتعديل: {reason}", ct);

        // Create a new Draft run over the same period, linked to the one it supersedes.
        var period = new PayrollPeriod(old.PeriodStart, old.PeriodEnd);
        var newRun = await _runEngine.CreateAsync(old.PayrollDefinitionId, period, ct);
        newRun.AmendsRunId = old.Id;
        old.SupersededByRunId = newRun.Id;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync("PayrollRunAmended", nameof(PayrollRun), runId,
            new { oldRun = old.RunNumber }, new { newRun = newRun.RunNumber, reason }, ct);

        return new AmendResult(old.Id, newRun.Id, newRun.RunNumber);
    }

    public async Task<int> ReissueAsync(Guid runId, CancellationToken ct = default)
    {
        _ = await _db.PayrollRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new NotFoundException("PayrollRun", runId);

        var count = await _payslips.ArchiveRunAsync(runId, ct);
        await _audit.LogAsync("PayrollRunReissued", nameof(PayrollRun), runId, null, new { payslips = count }, ct);
        return count;
    }
}
