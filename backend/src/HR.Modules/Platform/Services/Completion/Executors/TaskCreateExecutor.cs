using HR.Application.Engines.Completion;
using HR.Modules.Tasks.Entities;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;

namespace HR.Modules.Platform.Services.Completion.Executors;

/// <summary>
/// Creates a follow-up task from an approved request — "prepare the new hire's laptop", "book the
/// trip". Transactional: the task is part of what the approval means, so if a later effect fails
/// and the run rolls back, the orphan task goes with it.
/// </summary>
public sealed class TaskCreateExecutor : IEffectExecutor
{
    private readonly ApplicationDbContext _db;

    public TaskCreateExecutor(ApplicationDbContext db) => _db = db;

    public string EffectType => EffectTypes.TaskCreate;

    public Task<EffectExecutionResult> ExecuteAsync(EffectContext ctx, CancellationToken ct)
    {
        var title = ctx.Str("title");
        if (string.IsNullOrWhiteSpace(title))
            title = $"Follow-up: {ctx.RequestTypeCode} {ctx.RequestNumber}";

        var assignee = ctx.Guid("assigneeUserId");

        var task = new HrTask
        {
            Title = title,
            Description = ctx.Str("description"),
            Status = HrTaskStatus.NotStarted,
            Priority = TaskPriority.Medium,
            // Marks the task's provenance so it is distinguishable from one a person typed in.
            Source = TaskSource.System,
            DueDate = ctx.Date("dueDate"),
            AssigneeId = assignee == System.Guid.Empty ? null : assignee,
            AssignedById = ctx.ActorUserId,
            Category = "Request",
        };
        _db.Set<HrTask>().Add(task);

        return Task.FromResult(EffectExecutionResult.Ok(
            targetEntityType: "HrTask",
            targetRecordId: task.Id,
            after: new { task.Title, task.DueDate, task.AssigneeId },
            summary: $"Task '{title}' created"));
    }
}
