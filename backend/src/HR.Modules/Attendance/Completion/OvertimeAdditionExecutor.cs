using FluentValidation.Results;
using HR.Application.Common.Exceptions;
using HR.Application.Engines.Completion;
using HR.Application.Engines.Finance;
using HR.Domain.Engines.Finance.Entities;
using HR.Domain.Engines.MasterData;
using HR.Domain.Engines.Notifications;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Attendance.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Attendance.Completion;

/// <summary>Effect: on final approval of an overtime request, create a born-Approved OVERTIME
/// payroll Addition for the approved hours, at the KSA overtime rate. Paid exactly once: idempotent
/// per request instance, and guarded against the engine includeOvertime sync (which writes
/// SourceModule="Attendance"). Closed target period → PayrollAdjustmentNeeded notification, no
/// mutation.</summary>
public sealed class OvertimeAdditionExecutor : IEffectExecutor
{
    private readonly ApplicationDbContext _db;
    private readonly IOvertimeWageResolver _wage;
    private readonly IPayrollPeriodGuard _guard;

    public OvertimeAdditionExecutor(ApplicationDbContext db, IOvertimeWageResolver wage, IPayrollPeriodGuard guard)
    {
        _db = db;
        _wage = wage;
        _guard = guard;
    }

    public string EffectType => EffectTypes.OvertimeCreateAddition;

    public async Task<EffectExecutionResult> ExecuteAsync(EffectContext ctx, CancellationToken ct)
    {
        var date = DateTime.SpecifyKind(
            (ctx.Date("date") ?? throw Validation("date", "تاريخ العمل الإضافي مطلوب / Overtime date is required.")).Date,
            DateTimeKind.Utc);

        var hours = ctx.Dec("hours");
        if (hours <= 0m)
            throw Validation("hours", "عدد ساعات العمل الإضافي يجب أن يكون أكبر من صفر / Overtime hours must be greater than zero.");

        // ── Idempotency: one Addition per approved request instance ──────────────────────────────────
        var already = await _db.PayrollTransactions.AnyAsync(
            t => t.ReferenceType == "OvertimeRequest" && t.ReferenceId == ctx.RequestInstanceId, ct);
        if (already)
            return EffectExecutionResult.Skip("AlreadyApplied",
                targetEntityType: nameof(PayrollTransaction),
                summary: $"Overtime addition for request {ctx.RequestNumber} already created.");

        // ── Resolve the OVERTIME AdditionType (must be seeded) ───────────────────────────────────────
        var typeId = await _db.MasterDataItems
            .Where(m => m.ObjectType == MasterDataObjectType.AdditionType && m.Code == "OVERTIME")
            .Select(m => m.Id)
            .FirstOrDefaultAsync(ct);
        if (typeId == Guid.Empty)
            throw new NonRetryableEffectException(
                "OVERTIME addition type is not seeded; run master-data seed-defaults. " +
                "/ نوع الإضافة OVERTIME غير مُهيأ؛ شغّل تهيئة البيانات الأساسية.");

        // ── Double-pay guard: skip if the engine sync already paid overtime for this period ──────────
        var engineAlreadyPaid = await _db.PayrollTransactions.AnyAsync(
            t => t.EmployeeId == ctx.EmployeeId
              && t.TypeId == typeId
              && t.TargetPeriodYear == date.Year
              && t.TargetPeriodMonth == date.Month
              && t.SourceModule == "Attendance"
              && t.Status != PayrollTransactionStatus.Cancelled, ct);
        if (engineAlreadyPaid)
            return EffectExecutionResult.Skip("EngineOvertimeAlreadyPaid",
                targetEntityType: nameof(PayrollTransaction),
                summary: $"Engine overtime sync already paid {date:yyyy-MM} for this employee; request skipped to avoid double pay.");

        // ── Period guard: no born-Approved transaction into a frozen period ──────────────────────────
        bool periodClosed = false;
        try { await _guard.EnsurePeriodOpenForAsync(ctx.EmployeeId, date, ct); }
        catch (PayrollPeriodClosedException) { periodClosed = true; }

        var (hourlyWage, multiplier) = await _wage.ResolveAsync(ctx.EmployeeId, ct);
        var amount = Math.Round(hours * hourlyWage * multiplier, 2);

        if (periodClosed)
        {
            if (ctx.ActorUserId is { } signalUser)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = signalUser,
                    TitleAr = "عمل إضافي — فترة رواتب مقفلة",
                    TitleEn = "Overtime — Payroll Period Finalized",
                    BodyAr = $"تمت الموافقة على عمل إضافي ({hours:0.##} ساعة، {amount:0.##} ريال) بتاريخ {date:yyyy-MM-dd} لكن فترة الرواتب مقفلة. يلزم تسوية يدوية.",
                    BodyEn = $"Overtime ({hours:0.##}h, {amount:0.##} SAR) approved for {date:yyyy-MM-dd} but the payroll period is finalized. A manual payroll adjustment is required.",
                    Category = "PayrollAdjustmentNeeded",
                    Link = "/payroll",
                    IsRead = false,
                });
            }
            return EffectExecutionResult.Skip("PayrollPeriodFinalized",
                targetEntityType: nameof(PayrollTransaction),
                summary: $"Overtime for {date:yyyy-MM-dd} not posted — period finalized; adjustment notification emitted.");
        }

        _db.PayrollTransactions.Add(new PayrollTransaction
        {
            Kind = PayrollTransactionKind.Addition,
            EmployeeId = ctx.EmployeeId,
            TypeId = typeId,
            Amount = amount,
            EffectiveDate = date,
            TransactionDate = date,
            TargetPeriodYear = date.Year,
            TargetPeriodMonth = date.Month,
            SourceModule = "Overtime",
            ReferenceType = "OvertimeRequest",
            ReferenceId = ctx.RequestInstanceId,
            Status = PayrollTransactionStatus.Approved,
            Origin = PayrollTransactionOrigin.System,
            Notes = ctx.Str("reason"),
        });

        return EffectExecutionResult.Ok(
            targetEntityType: nameof(PayrollTransaction),
            after: new { EmployeeId = ctx.EmployeeId, Amount = amount, date.Year, date.Month, Hours = hours },
            summary: $"Overtime addition {amount:0.##} SAR ({hours:0.##}h) recorded for {date:yyyy-MM}.");
    }

    private static ValidationException Validation(string field, string message)
        => new(new[] { new ValidationFailure(field, message) });
}
