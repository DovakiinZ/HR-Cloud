using HR.Application.Engines.Timeline;
using HR.Domain.Engines.Timeline;

namespace HR.Infrastructure.Engines.Timeline;

/// <summary>Projects employee field changes into categorized timeline events (spec Feature 4).
/// Only registered, journey-worthy fields produce events; unknown fields are ignored so the timeline
/// is built from meaningful changes rather than duplicated text. The acting user is stamped by
/// <see cref="ITimelineEngine.PublishEvent"/> from the current-user context.</summary>
public class TimelineProjectionService : ITimelineProjectionService
{
    private readonly ITimelineEngine _timeline;

    public TimelineProjectionService(ITimelineEngine timeline) => _timeline = timeline;

    private sealed record FieldMeta(TimelineCategory Category, string DescEn, string DescAr);

    // Registered employee fields whose changes belong on the journey timeline.
    private static readonly IReadOnlyDictionary<string, FieldMeta> Map =
        new Dictionary<string, FieldMeta>(StringComparer.OrdinalIgnoreCase)
        {
            ["DepartmentId"] = new(TimelineCategory.Assignment, "Department changed", "تغيير القسم"),
            ["BranchId"] = new(TimelineCategory.Assignment, "Branch changed", "تغيير الفرع"),
            ["ManagerId"] = new(TimelineCategory.Assignment, "Manager changed", "تغيير المدير المباشر"),
            ["JobTitleId"] = new(TimelineCategory.Assignment, "Job title changed", "تغيير المسمى الوظيفي"),
            ["BasicSalary"] = new(TimelineCategory.Compensation, "Salary changed", "تغيير الراتب"),
        };

    public async Task ProjectEmployeeChangeAsync(Guid employeeId, object before, object after,
        Guid? actorId, CancellationToken ct = default)
    {
        if (before is null || after is null) return;

        var beforeProps = before.GetType().GetProperties()
            .ToDictionary(p => p.Name, p => p.GetValue(before), StringComparer.OrdinalIgnoreCase);

        foreach (var ap in after.GetType().GetProperties())
        {
            if (!Map.TryGetValue(ap.Name, out var meta)) continue;

            var afterVal = ap.GetValue(after);
            beforeProps.TryGetValue(ap.Name, out var beforeVal);
            if (Equals(beforeVal, afterVal)) continue;

            await _timeline.PublishEvent(
                meta.Category.ToString(), "Employee", employeeId, $"{ap.Name}Changed",
                meta.DescEn, meta.DescAr,
                new { field = ap.Name, before = beforeVal, after = afterVal }, ct);
        }
    }
}
