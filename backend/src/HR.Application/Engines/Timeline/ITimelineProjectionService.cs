namespace HR.Application.Engines.Timeline;

/// <summary>Turns an employee before/after diff into categorized <see cref="Domain.Engines.Timeline
/// .TimelineEvent"/>s (spec Feature 4). The timeline is built from real changes, not duplicated text.</summary>
public interface ITimelineProjectionService
{
    /// <summary>Diffs <paramref name="before"/> vs <paramref name="after"/> and publishes one timeline
    /// event per changed, journey-worthy field via <see cref="ITimelineEngine.PublishEvent"/>.</summary>
    Task ProjectEmployeeChangeAsync(Guid employeeId, object before, object after, Guid? actorId,
        CancellationToken ct = default);
}
