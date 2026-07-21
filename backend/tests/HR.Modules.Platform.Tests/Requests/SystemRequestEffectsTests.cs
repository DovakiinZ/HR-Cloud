using FluentAssertions;
using HR.Application.Engines.Completion;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Requests;
using Xunit;

namespace HR.Modules.Platform.Tests.Requests;

/// <summary>Locks the required-effect declarations that provisioning reconciles onto system
/// request types. These are load-bearing: they are what turns an approval into real work.</summary>
public class SystemRequestEffectsTests
{
    private static RequiredEffectSpec Effect(string code, string effectType) =>
        SystemRequestEffects.Required[code].Single(e => e.EffectType == effectType);

    [Theory]
    [InlineData("RESIGNATION")]
    [InlineData("CLEARANCE")]
    [InlineData("COMPLAINT")]
    public void Lifecycle_requests_create_a_follow_up_task(string code)
    {
        SystemRequestEffects.Required.Should().ContainKey(code);
        var task = Effect(code, EffectTypes.TaskCreate);

        task.ExecutionMode.Should().Be(EffectExecutionMode.Transactional);
        task.Trigger.Should().Be(EffectTrigger.FinalApproval);

        // The title is a fixed business label — a constant, never a raw form-field dependency.
        task.Inputs["title"].Source.Should().Be(EffectValueSource.Constant);
        task.Inputs["title"].Key.Should().NotBeNullOrWhiteSpace();

        // Assigned to the acting approver (bindable, so tenants can re-point it).
        task.Inputs["assigneeUserId"].Source.Should().Be(EffectValueSource.CurrentUser);
        task.Inputs["assigneeUserId"].Key.Should().Be(CurrentUserKeys.UserId);
    }

    [Theory]
    [InlineData("RESIGNATION")]
    [InlineData("COMPLAINT")]
    public void Resignation_and_complaint_notify_the_requester_asynchronously(string code)
    {
        var note = Effect(code, EffectTypes.NotificationSend);

        // Async so a mail transport failure can never roll back the approval.
        note.ExecutionMode.Should().Be(EffectExecutionMode.Asynchronous);
        note.Inputs["subject"].Source.Should().Be(EffectValueSource.Constant);
        note.Inputs["body"].Source.Should().Be(EffectValueSource.Constant);
        // No explicit recipient — the executor defaults to the requesting employee.
        note.Inputs.Should().NotContainKey("toEmail");
    }

    [Fact]
    public void Effect_types_used_are_all_real_catalog_actions()
    {
        // Every effect declared must reference a known EffectTypes constant (guards typos).
        var known = new[]
        {
            EffectTypes.LeaveCreateApprovedLeave, EffectTypes.AttendanceApplyLeaveDays,
            EffectTypes.AttendanceCreatePunch, EffectTypes.AttendanceCorrect,
            EffectTypes.ExpenseCreateClaim, EffectTypes.LoanCreate, EffectTypes.AssetsAssignCustody,
            EffectTypes.NotificationSend, EffectTypes.TaskCreate,
        };
        var used = SystemRequestEffects.Required.Values.SelectMany(v => v).Select(e => e.EffectType);
        used.Should().OnlyContain(t => known.Contains(t));
    }
}
