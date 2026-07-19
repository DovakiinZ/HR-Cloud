using System;
using FluentAssertions;
using HR.Application.Engines.Notifications;
using HR.Domain.Engines.Notifications;
using HR.Domain.Enums;
using Xunit;

namespace HR.Modules.Platform.Tests.Notifications;

public class EmailDeliveryDecisionTests
{
    private static readonly DateTime Now = new(2026, 7, 17, 10, 0, 0, DateTimeKind.Utc);
    private static EmailNotificationQueue Row(int attempts = 0) => new()
        { ToEmail = "a@b.com", Subject = "s", Body = "b", Attempts = attempts, Status = EmailQueueStatus.Pending };

    [Fact]
    public void Success_marks_Sent_and_stamps_time()
    {
        var row = Row();
        EmailDeliveryDecision.Apply(row, EmailSendResult.Ok(), maxAttempts: 5, Now);
        row.Status.Should().Be(EmailQueueStatus.Sent);
        row.SentAt.Should().Be(Now);
        row.Error.Should().BeNull();
    }

    [Fact]
    public void Failure_below_cap_increments_and_stays_Pending()
    {
        var row = Row(attempts: 1);
        EmailDeliveryDecision.Apply(row, EmailSendResult.Fail("boom"), maxAttempts: 5, Now);
        row.Status.Should().Be(EmailQueueStatus.Pending);
        row.Attempts.Should().Be(2);
        row.Error.Should().Be("boom");
        row.SentAt.Should().BeNull();
    }

    [Fact]
    public void Failure_reaching_cap_marks_Failed()
    {
        var row = Row(attempts: 4);
        EmailDeliveryDecision.Apply(row, EmailSendResult.Fail("boom"), maxAttempts: 5, Now);
        row.Attempts.Should().Be(5);
        row.Status.Should().Be(EmailQueueStatus.Failed);
    }
}
