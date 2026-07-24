namespace HR.Application.Engines.Completion;

/// <summary>Claims and executes due deferred completion effects, one worker tick. Returns how many effects
/// were processed (completed, skipped, retried, or sent to manual review) this tick.</summary>
public interface IScheduledEffectDrainer
{
    Task<int> DrainAsync(CancellationToken ct);
}
