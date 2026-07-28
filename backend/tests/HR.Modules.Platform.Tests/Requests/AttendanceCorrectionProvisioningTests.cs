using FluentAssertions;
using HR.Domain.Enums;
using HR.Modules.Platform.Services.Requests;
using Xunit;

namespace HR.Modules.Platform.Tests.Requests;

public class AttendanceCorrectionProvisioningTests
{
    [Fact]
    public void Seeds_five_attendance_correction_rules()
    {
        var rules = SystemWorkflowNotificationRules.For("ATTENDANCE_CORRECTION");
        rules.Should().HaveCount(5);
        rules.Select(r => r.Event).Should().BeEquivalentTo(new[]
        {
            WorkflowNotificationEvent.Submitted,
            WorkflowNotificationEvent.StepAssigned,
            WorkflowNotificationEvent.Rejected,
            WorkflowNotificationEvent.Returned,
            WorkflowNotificationEvent.FinalApproved,
        });
        rules.Single(r => r.Event == WorkflowNotificationEvent.StepAssigned)
             .Recipients.Single().Type.Should().Be(NotificationRecipientType.CurrentApprover);
        rules.Where(r => r.Event != WorkflowNotificationEvent.StepAssigned)
             .Should().OnlyContain(r => r.Recipients.Single().Type == NotificationRecipientType.Requester);
        rules.Select(r => r.SystemKey).Should().OnlyHaveUniqueItems();
    }
}
