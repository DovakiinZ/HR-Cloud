using System;
using FluentAssertions;
using HR.Modules.Platform.Services.SemanticCatalog;
using Xunit;

namespace HR.Modules.Platform.Tests.SemanticCatalog;

public class RelativeDateTests
{
    private static readonly DateTime Now = new(2026, 7, 18, 13, 45, 0, DateTimeKind.Utc);

    [Fact] public void Today_is_date_floor()
        => RelativeDate.Resolve("today", Now).Should().Be(new DateTime(2026, 7, 18, 0, 0, 0, DateTimeKind.Utc));
    [Fact] public void Plus_days()
        => RelativeDate.Resolve("today+30d", Now).Should().Be(new DateTime(2026, 8, 17, 0, 0, 0, DateTimeKind.Utc));
    [Fact] public void Minus_days()
        => RelativeDate.Resolve("today-7d", Now).Should().Be(new DateTime(2026, 7, 11, 0, 0, 0, DateTimeKind.Utc));
    [Fact] public void Start_of_month()
        => RelativeDate.Resolve("startOfMonth", Now).Should().Be(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
    [Fact] public void End_of_month()
        => RelativeDate.Resolve("endOfMonth", Now).Should().Be(new DateTime(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc));

    [Fact] public void Unknown_token_throws()
        => FluentActions.Invoking(() => RelativeDate.Resolve("nonsense", Now)).Should().Throw<FormatException>();
}
