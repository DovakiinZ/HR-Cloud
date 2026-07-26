using FluentAssertions;
using HR.Application.Engines.Notifications;
using Xunit;

namespace HR.Modules.Platform.Tests.Notifications;

public class NotificationTokenWhitelistTests
{
    [Fact]
    public void Known_token_is_allowed()
        => NotificationTokenWhitelist.FindUnknownTokens("Hello {{Employee.FullName}}").Should().BeEmpty();

    [Fact]
    public void Unknown_token_is_reported()
        => NotificationTokenWhitelist.FindUnknownTokens("{{Secret.Password}}").Should().Contain("Secret.Password");

    [Fact]
    public void Plain_text_has_no_unknown_tokens()
        => NotificationTokenWhitelist.FindUnknownTokens("no tokens here").Should().BeEmpty();
}
