using System.Text.Json;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Domain.Enums;
using HR.Domain.Engines.Requests;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Requests;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Completion;

/// <summary>
/// Builds the ordered list of completion-effect intents for a request.
///
/// Two sources, in priority order:
///
/// 1. <see cref="RequestEffectDefinition"/> — configured actions from the Effect Action Catalog,
///    with their inputs resolved through <see cref="EffectValueResolver"/>. This is how every
///    dynamic request type works.
/// 2. <see cref="RequestImpactMapping"/> — the original boolean flags. Kept as a fallback for
///    request types that have not been migrated to configured effects, so nothing that works today
///    stops working. A type with any enabled definition uses only source 1: mixing them would run
///    the leave effect twice.
///
/// The engine and executors stay generic — this is the only place that knows how a request type's
/// configuration becomes an intent.
/// </summary>
public sealed class CompletionEffectFactory : ICompletionEffectFactory
{
    private readonly ApplicationDbContext _db;
    private readonly ILeaveService _leave;
    private readonly ICurrentUserService _user;
    private static readonly JsonSerializerOptions Json = new(); // member names as-written (camelCase)

    public CompletionEffectFactory(ApplicationDbContext db, ILeaveService leave, ICurrentUserService user)
    {
        _db = db;
        _leave = leave;
        _user = user;
    }

    public async Task<IReadOnlyList<EffectIntent>> BuildAsync(Guid requestInstanceId, CancellationToken ct)
    {
        var instance = await _db.RequestInstances
            .Include(r => r.RequestType).ThenInclude(t => t.ImpactMapping)
            .FirstOrDefaultAsync(r => r.Id == requestInstanceId, ct)
            ?? throw new InvalidOperationException($"RequestInstance {requestInstanceId} not found.");

        var type = instance.RequestType;

        // Configured effects win outright when present.
        var configured = await BuildFromDefinitionsAsync(instance, type, EffectTrigger.FinalApproval, ct);
        if (configured.Count > 0) return configured;

        var impact = type.ImpactMapping;
        var intents = new List<EffectIntent>();
        if (impact is null) return intents;

        // Leave-type rules drive the leave/attendance decisions (object-driven, never hardcoded).
        LeaveRules? rules = null;
        if (instance.LeaveTypeId is { } ltId)
        {
            var leaveItem = await _db.MasterDataItems.FirstOrDefaultAsync(m => m.Id == ltId, ct);
            rules = _leave.GetRules(leaveItem?.MetadataJson);
        }

        var seq = 0;

        // 1) Leave: create the approved leave record (+ balance), then mark attendance days.
        if (instance.LeaveTypeId is { } leaveTypeId && instance.StartDate is { } start && instance.EndDate is { } end)
        {
            var days = instance.DaysCount ?? 0m;
            var affectsBalance = impact.AffectsLeaveBalance && days > 0 && (rules?.Paid ?? true);

            intents.Add(new EffectIntent(EffectTypes.LeaveCreateApprovedLeave, ++seq, Serialize(new
            {
                leaveTypeId,
                startDate = start,
                endDate = end,
                daysCount = days,
                affectsBalance,
                entitledFallback = (decimal)(rules?.AnnualBalance ?? 30),
                notes = instance.DecisionNote,
            })));

            var marksAttendance = impact.AffectsAttendance && (rules?.AffectsAttendance ?? true);
            if (marksAttendance && end.Date >= start.Date)
                intents.Add(new EffectIntent(EffectTypes.AttendanceApplyLeaveDays, ++seq, Serialize(new
                {
                    startDate = start,
                    endDate = end,
                })));
        }

        // 2) Non-leave targets read submitted form values.
        var needsForm = impact.CreatesExpenseRecord || impact.CreatesLoanRecord || impact.CreatesAttendancePunch
            || (impact.AffectsAttendance && instance.LeaveTypeId is null);
        if (needsForm)
        {
            var fv = await LoadFormValuesAsync(instance.FormSubmissionId, ct);
            string? V(string code) => fv.TryGetValue(code, out var x) ? x.Value : null;
            string? Fl(string code) => fv.TryGetValue(code, out var x) ? x.FileUrl : null;

            if (impact.CreatesExpenseRecord)
            {
                var currency = await _db.Employees.Where(e => e.Id == instance.EmployeeId)
                    .Select(e => e.Currency).FirstOrDefaultAsync(ct) ?? "SAR";
                intents.Add(new EffectIntent(EffectTypes.ExpenseCreateClaim, ++seq, Serialize(new
                {
                    expenseCategory = V("expenseCategory"),
                    amount = V("amount"),
                    description = V("description") ?? V("reason"),
                    receipt = Fl("receipt"),
                    currency,
                })));
            }

            if (impact.CreatesLoanRecord)
            {
                var isAdvance = type.Code == "SALARY_ADVANCE";
                intents.Add(new EffectIntent(EffectTypes.LoanCreate, ++seq, Serialize(new
                {
                    loanType = V("loanType"),
                    amount = V("amount"),
                    installmentMonths = isAdvance ? "1" : V("installmentMonths"),
                    kind = isAdvance ? "Advance" : "Loan",
                })));
            }

            if (impact.CreatesAttendancePunch)
            {
                intents.Add(new EffectIntent(EffectTypes.AttendanceCreatePunch, ++seq, Serialize(new
                {
                    date = V("startDate") ?? V("date"),
                    checkIn = V("checkIn"),
                    checkOut = V("checkOut"),
                    reason = V("reason"),
                })));
            }
            else if (impact.AffectsAttendance && instance.LeaveTypeId is null)
            {
                intents.Add(new EffectIntent(EffectTypes.AttendanceCorrect, ++seq, Serialize(new
                {
                    date = V("startDate"),
                    reason = V("reason"),
                })));
            }
        }

        return intents;
    }

    /// <summary>
    /// Turns the request type's configured effects into intents for one trigger.
    ///
    /// Ordering is (Sequence, Id): Sequence is what the builder reorders, and Id breaks ties so two
    /// effects sharing a sequence number still execute in a stable order rather than whatever the
    /// database happens to return.
    ///
    /// Disabled effects are skipped. An effect whose action is no longer in the catalog is skipped
    /// rather than thrown on — the request has already been approved, and failing completion because
    /// a descriptor was retired would strand it in CompletionFailed. Activation-time validation is
    /// where an unknown action is supposed to be caught.
    /// </summary>
    private async Task<List<EffectIntent>> BuildFromDefinitionsAsync(
        RequestInstance instance, RequestType type, EffectTrigger trigger, CancellationToken ct)
    {
        var definitions = await _db.Set<RequestEffectDefinition>()
            .Where(e => e.RequestTypeId == type.Id && e.Trigger == trigger && e.IsEnabled)
            .OrderBy(e => e.Sequence).ThenBy(e => e.Id)
            .ToListAsync(ct);

        var intents = new List<EffectIntent>();
        if (definitions.Count == 0) return intents;

        var ctx = new EffectResolutionContext
        {
            Instance = instance,
            RequestTypeCode = type.Code,
            TenantId = instance.TenantId,
            ActorUserId = _user.UserId,
            FormValues = await LoadFormValuesAsync(instance.FormSubmissionId, ct),
        };

        var seq = 0;
        foreach (var def in definitions)
        {
            var config = EffectConfiguration.TryParse(def.ConfigurationJson);
            if (config is null) continue;   // malformed configuration: nothing safe to run

            var payload = EffectValueResolver.Resolve(config, ctx);
            intents.Add(new EffectIntent(def.EffectType, ++seq, Serialize(payload)));
        }

        return intents;
    }

    private static string Serialize(object payload) => JsonSerializer.Serialize(payload, Json);

    /// <summary>Load a submission's values into a fieldCode → (value, fileUrl) map.</summary>
    private async Task<Dictionary<string, (string? Value, string? FileUrl)>> LoadFormValuesAsync(Guid submissionId, CancellationToken ct)
    {
        var vals = await _db.FormSubmissionValues.Where(v => v.FormSubmissionId == submissionId)
            .Select(v => new { v.FieldCode, v.Value, v.FileUrl }).ToListAsync(ct);
        return vals.GroupBy(v => v.FieldCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (g.Last().Value, g.Last().FileUrl), StringComparer.OrdinalIgnoreCase);
    }
}
