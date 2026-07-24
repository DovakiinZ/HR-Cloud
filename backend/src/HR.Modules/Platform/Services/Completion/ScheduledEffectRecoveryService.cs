using HR.Application.Engines.Timeline;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Completion;

/// <summary>
/// Operator recovery for deferred effects that landed in <c>ManualReview</c> or <c>Failed</c>.
/// Provides list / retry / skip so an admin can resolve stuck effects without direct DB access.
/// </summary>
public sealed class ScheduledEffectRecoveryService : IScheduledEffectRecoveryService
{
    private readonly ApplicationDbContext _db;
    private readonly ITimelineEngine _timeline;

    public ScheduledEffectRecoveryService(ApplicationDbContext db, ITimelineEngine timeline)
    {
        _db = db;
        _timeline = timeline;
    }

    public async Task<IReadOnlyList<AttentionEffectDto>> ListAttentionAsync(CancellationToken ct) =>
        await _db.CompletionEffects
            .Where(e => e.IdempotencyKey != null
                     && (e.Status == CompletionEffectStatus.ManualReview
                      || e.Status == CompletionEffectStatus.Failed))
            .OrderBy(e => e.ExecutedAt)
            .Select(e => new AttentionEffectDto(
                e.Id,
                e.RequestInstanceId,
                e.EffectType,
                e.Attempts,
                e.MaxAttempts,
                e.FailureReason,
                e.ScheduledFor))
            .ToListAsync(ct);

    public async Task<bool> RetryAsync(Guid effectId, CancellationToken ct)
    {
        var effect = await _db.CompletionEffects.FirstOrDefaultAsync(e => e.Id == effectId, ct);
        if (effect is null
            || effect.IdempotencyKey is null
            || effect.Status is not (CompletionEffectStatus.ManualReview or CompletionEffectStatus.Failed))
            return false;

        effect.Status = CompletionEffectStatus.Pending;
        effect.NextAttemptAt = DateTime.UtcNow;
        effect.LeasedBy = null;
        effect.LeasedUntil = null;
        effect.FailureReason = null;
        effect.Attempts = 0;
        await _db.SaveChangesAsync(ct);

        await _timeline.PublishEvent(
            "Completion",
            "RequestInstance",
            effect.RequestInstanceId,
            "EffectRequeued",
            $"Deferred effect {effect.EffectType} was manually requeued for retry",
            $"تمت إعادة جدولة الإجراء {effect.EffectType} يدويًا",
            new { effectId = effect.Id },
            ct);

        return true;
    }

    public async Task<bool> SkipAsync(Guid effectId, string reason, CancellationToken ct)
    {
        var effect = await _db.CompletionEffects.FirstOrDefaultAsync(e => e.Id == effectId, ct);
        if (effect is null
            || effect.IdempotencyKey is null
            || effect.Status is not (CompletionEffectStatus.ManualReview or CompletionEffectStatus.Failed))
            return false;

        effect.Status = CompletionEffectStatus.Skipped;
        effect.FailureReason = reason;
        effect.LeasedBy = null;
        effect.LeasedUntil = null;
        await _db.SaveChangesAsync(ct);

        await _timeline.PublishEvent(
            "Completion",
            "RequestInstance",
            effect.RequestInstanceId,
            "EffectSkipped",
            $"Deferred effect {effect.EffectType} was manually skipped: {reason}",
            $"تم تخطّي الإجراء {effect.EffectType} يدويًا: {reason}",
            new { effectId = effect.Id },
            ct);

        return true;
    }
}
