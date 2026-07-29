using FluentAssertions;
using HR.Application.Common.Models;
using HR.Application.Engines.Timeline;
using HR.Domain.Engines.Timeline;
using HR.Infrastructure.Engines.Timeline;
using Xunit;

namespace HR.Modules.Platform.Tests.Timeline;

public class TimelineProjectionServiceTests
{
    private sealed record Captured(string Category, string EntityType, Guid EntityId, string Action,
        IReadOnlyDictionary<string, object?> Metadata);

    /// <summary>Test double: captures the raw metadata object as a dictionary so we can assert on it
    /// (the real engine serializes it to a JSON string).</summary>
    private sealed class CapturingTimelineEngine : ITimelineEngine
    {
        public List<Captured> Published { get; } = new();

        public Task PublishEvent(string category, string entityType, Guid entityId, string action,
            string? descriptionEn = null, string? descriptionAr = null, object? metadata = null,
            CancellationToken ct = default)
        {
            var dict = new Dictionary<string, object?>();
            if (metadata != null)
                foreach (var p in metadata.GetType().GetProperties())
                    dict[p.Name] = p.GetValue(metadata);
            Published.Add(new Captured(category, entityType, entityId, action, dict));
            return Task.CompletedTask;
        }

        public Task<PaginatedList<TimelineEvent>> GetTimeline(string entityType, Guid entityId,
            int pageNumber = 1, int pageSize = 20, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task Department_change_projects_Assignment_event_with_before_after()
    {
        var engine = new CapturingTimelineEngine();
        var svc = new TimelineProjectionService(engine);
        var before = new { DepartmentId = Guid.NewGuid(), BasicSalary = 5000m };
        var after = new { DepartmentId = Guid.NewGuid(), BasicSalary = 5000m };

        await svc.ProjectEmployeeChangeAsync(Guid.NewGuid(), before, after, actorId: null, default);

        var e = engine.Published.Single();
        e.Category.Should().Be(nameof(TimelineCategory.Assignment));
        e.Metadata.Should().ContainKey("field"); // "DepartmentId"
    }

    [Fact]
    public async Task Salary_change_projects_Compensation_category()
    {
        var engine = new CapturingTimelineEngine();
        await new TimelineProjectionService(engine).ProjectEmployeeChangeAsync(
            Guid.NewGuid(), new { BasicSalary = 5000m }, new { BasicSalary = 6000m }, null, default);
        engine.Published.Single().Category.Should().Be(nameof(TimelineCategory.Compensation));
    }

    [Fact]
    public async Task No_change_publishes_nothing()
    {
        var engine = new CapturingTimelineEngine();
        await new TimelineProjectionService(engine).ProjectEmployeeChangeAsync(
            Guid.NewGuid(), new { BasicSalary = 5000m }, new { BasicSalary = 5000m }, null, default);
        engine.Published.Should().BeEmpty();
    }
}
