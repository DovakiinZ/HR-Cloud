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
            EffectTypes.AttendanceCreatePermission,
            EffectTypes.ExpenseCreateClaim, EffectTypes.LoanCreate, EffectTypes.AssetsAssignCustody,
            EffectTypes.NotificationSend, EffectTypes.TaskCreate,
        };
        var used = SystemRequestEffects.Required.Values.SelectMany(v => v).Select(e => e.EffectType);
        used.Should().OnlyContain(t => known.Contains(t));
    }

    // ─── ATTENDANCE_PERMISSION ────────────────────────────────────────────────

    [Fact]
    public void Attendance_permission_is_declared_in_required_effects()
    {
        SystemRequestEffects.Required.Should().ContainKey("ATTENDANCE_PERMISSION",
            because: "the ATTENDANCE_PERMISSION system request type must have a required effect");
    }

    [Fact]
    public void Attendance_permission_maps_to_create_permission_effect()
    {
        var spec = SystemRequestEffects.Required["ATTENDANCE_PERMISSION"]
            .Single(s => s.EffectType == EffectTypes.AttendanceCreatePermission);

        spec.Trigger.Should().Be(EffectTrigger.FinalApproval,
            because: "the permission row must be created on final approval");
        spec.ExecutionMode.Should().Be(EffectExecutionMode.Transactional,
            because: "the permission row must be written in the same transaction as the approval");
    }

    [Fact]
    public void Attendance_permission_effect_maps_all_six_form_inputs()
    {
        var spec = SystemRequestEffects.Required["ATTENDANCE_PERMISSION"]
            .Single(s => s.EffectType == EffectTypes.AttendanceCreatePermission);

        var expectedKeys = new[] { "permissionTypeId", "date", "fromTime", "toTime", "reason", "overrideReason" };
        spec.Inputs.Keys.Should().Contain(expectedKeys,
            because: "the executor reads all six keys from the effect payload");

        // The lookup value is stored under "permissionType" in the form; the effect key is "permissionTypeId".
        spec.Inputs["permissionTypeId"].Source.Should().Be(EffectValueSource.FormField);
        spec.Inputs["permissionTypeId"].Key.Should().Be("permissionType");

        foreach (var key in new[] { "date", "fromTime", "toTime", "reason", "overrideReason" })
        {
            spec.Inputs[key].Source.Should().Be(EffectValueSource.FormField,
                because: $"'{key}' is sourced directly from the submitted form");
            spec.Inputs[key].Key.Should().Be(key,
                because: $"the form field code and the effect input key are identical for '{key}'");
        }
    }
}
