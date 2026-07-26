using HR.Application.Engines.Notifications;
using HR.Domain.Engines.Notifications;
using HR.Domain.Engines.Requests;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Documents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Modules.Platform.Services.Notifications;

/// <summary>
/// Centralized "event → rule → resolver → delivery" dispatcher.
///
/// Precedence (most-specific non-empty tier wins):
///   type+event+step  →  type+event  →  global+event+step  →  global+event
///
/// Claim-first dedup: each user is claimed to the FIRST rule (in Id order) that resolves them,
/// guaranteeing one delivery per user per event even when multiple rules match.
///
/// Idempotency: a (request, event, step, rule, user) dispatch row is written before delivery; the
/// combined Local+persisted check handles both in-process replays and cross-process duplicates.
///
/// Failure isolation: the entire method body is wrapped in a swallowing try/catch so a notification
/// problem can never roll back a request-lifecycle transition.
/// </summary>
public sealed class WorkflowNotificationDispatcher : IWorkflowNotificationDispatcher
{
    private const int StepAgnostic = -1;

    private readonly ApplicationDbContext _db;
    private readonly INotificationRecipientResolver _resolver;
    private readonly INotificationService _notifier;
    private readonly IRequestTokenResolver _tokens;
    private readonly ILogger<WorkflowNotificationDispatcher> _log;

    public WorkflowNotificationDispatcher(
        ApplicationDbContext db,
        INotificationRecipientResolver resolver,
        INotificationService notifier,
        IRequestTokenResolver tokens,
        ILogger<WorkflowNotificationDispatcher> log)
    {
        _db = db; _resolver = resolver; _notifier = notifier; _tokens = tokens; _log = log;
    }

    public async Task DispatchAsync(
        WorkflowNotificationEvent evt,
        RequestInstance instance,
        RequestApproval? step,
        CancellationToken ct)
    {
        try
        {
            var code = await _db.RequestTypes
                .Where(t => t.Id == instance.RequestTypeId)
                .Select(t => t.Code)
                .FirstOrDefaultAsync(ct);

            var stepOrder = step?.StepOrder;

            // 1. Winning tier — deterministic order by Id so claims are stable.
            var rules = (await SelectWinningTierAsync(code, evt, stepOrder, ct))
                .OrderBy(r => r.Id)
                .ToList();
            if (rules.Count == 0) return;

            // 2. Claim each user to the FIRST rule (in Id order) that resolves them.
            //    One delivery per user per event — regardless of how many rules matched.
            var claims = new Dictionary<Guid, WorkflowNotificationRule>();
            foreach (var rule in rules)
            {
                var parsed = RecipientSpecParser.ParseAndValidate(rule.RecipientsJson);
                if (!parsed.IsValid)
                {
                    _log.LogWarning("Skipping rule {RuleId}: invalid RecipientsJson: {Errors}",
                        rule.Id, string.Join("; ", parsed.Errors));
                    continue;
                }

                foreach (var spec in parsed.Envelope!.Recipients)
                {
                    IReadOnlyList<Guid> resolved;
                    try
                    {
                        resolved = await _resolver.ResolveAsync(spec, instance, step, ct);
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Recipient {Type} failed to resolve for {Req} — skipped.",
                            spec.Type, instance.RequestNumber);
                        continue;
                    }

                    if (resolved.Count == 0)
                    {
                        _log.LogInformation(
                            "Recipient {Type} unresolved for {Req}/{Evt} — skipped.",
                            spec.Type, instance.RequestNumber, evt);
                        continue;
                    }

                    foreach (var uid in resolved)
                        if (!claims.ContainsKey(uid))
                            claims[uid] = rule;
                }
            }

            if (claims.Count == 0) return;

            var tokens = await SafeTokensAsync(instance.Id, ct);
            var stepKey = stepOrder ?? StepAgnostic;

            // 3. Deliver once per claimed user — dual-visibility idempotency (Local + persisted).
            foreach (var (uid, rule) in claims)
            {
                bool already =
                    _db.WorkflowNotificationDispatches.Local.Any(d =>
                        d.RequestInstanceId == instance.Id &&
                        d.Event == evt &&
                        d.StepOrder == stepKey &&
                        d.RuleId == rule.Id &&
                        d.UserId == uid)
                    || await _db.WorkflowNotificationDispatches.AnyAsync(d =>
                        d.RequestInstanceId == instance.Id &&
                        d.Event == evt &&
                        d.StepOrder == stepKey &&
                        d.RuleId == rule.Id &&
                        d.UserId == uid, ct);

                if (already) continue;

                _db.WorkflowNotificationDispatches.Add(new WorkflowNotificationDispatch
                {
                    Id = Guid.NewGuid(),
                    TenantId = instance.TenantId,
                    RequestInstanceId = instance.Id,
                    Event = evt,
                    StepOrder = stepKey,
                    RuleId = rule.Id,
                    UserId = uid,
                    DispatchedAt = DateTime.UtcNow,
                });

                var subjAr = Render(rule.SubjectAr, tokens);
                var subjEn = Render(rule.SubjectEn, tokens);
                var bodyAr = Render(rule.BodyAr, tokens);
                var bodyEn = Render(rule.BodyEn, tokens);

                // TODO(SP-templates): honor rule.ChannelBell — the reused INotificationService.NotifyAsync
                // always writes a bell row; only the email channel is gated today. All seeded rules are
                // bell+email so this is inert until the notification-template admin UI can set ChannelBell=false.
                await _notifier.NotifyAsync(
                    uid, subjAr, subjEn, bodyAr, bodyEn,
                    "RequestWorkflow", instance.Id,
                    $"/requests/{instance.Id}",
                    email: rule.ChannelEmail,
                    ct: ct);
            }
        }
        catch (Exception ex)
        {
            // Absolute guarantee: a notification failure NEVER propagates to the workflow caller.
            _log.LogError(ex,
                "Workflow notification dispatch failed for request {Req}, event {Evt} — swallowed.",
                instance.RequestNumber, evt);
        }
    }

    /// <summary>
    /// Returns the most-specific non-empty tier of active rules for the given event.
    /// Tier order (first non-empty wins):
    ///   1. type+event+step  (RequestTypeCode != null  AND  StepOrder != null)
    ///   2. type+event       (RequestTypeCode != null  AND  StepOrder == null)
    ///   3. global+event+step (RequestTypeCode == null AND  StepOrder != null)
    ///   4. global+event     (RequestTypeCode == null  AND  StepOrder == null)
    /// </summary>
    private async Task<IReadOnlyList<WorkflowNotificationRule>> SelectWinningTierAsync(
        string? code, WorkflowNotificationEvent evt, int? step, CancellationToken ct)
    {
        // Pull every candidate that could possibly match (let the DB narrow by event + null-or-match).
        var candidates = await _db.WorkflowNotificationRules
            .Where(r => r.IsActive && r.Event == evt
                && (r.RequestTypeCode == null || r.RequestTypeCode == code)
                && (r.StepOrder == null || r.StepOrder == step))
            .ToListAsync(ct);

        // Walk tiers from most-specific to least-specific; return first non-empty.
        foreach (var (typed, stepped) in new[]
            { (true, true), (true, false), (false, true), (false, false) })
        {
            var tier = candidates
                .Where(r =>
                    (r.RequestTypeCode != null) == typed &&
                    (r.StepOrder != null) == stepped)
                .ToList();

            if (tier.Count > 0) return tier;
        }

        return Array.Empty<WorkflowNotificationRule>();
    }

    private async Task<IReadOnlyDictionary<string, string>> SafeTokensAsync(
        Guid instanceId, CancellationToken ct)
    {
        try { return await _tokens.ResolveForRequestAsync(instanceId, ct); }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "Token resolution failed for request {Id}; rendering with no tokens.", instanceId);
            return new Dictionary<string, string>();
        }
    }

    private static string Render(string template, IReadOnlyDictionary<string, string> tokens)
        => DocumentRenderer.ResolveTokens(template ?? "", tokens);
}
