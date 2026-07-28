using System.Text.Json;
using FluentAssertions;
using HR.Application.Common.Interfaces;
using HR.Application.Engines.Completion;
using HR.Domain.Engines.Attendance;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Attendance.Completion;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HR.Domain.Finance.Tests;

public class AttendanceCorrectionExecutorTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("08:00", true)]
    [InlineData("23:59", true)]
    [InlineData("00:00", true)]
    [InlineData("25:00", false)]
    [InlineData("8am", false)]
    [InlineData("8:5", false)]
    public void PunchTime_validates_hhmm(string? input, bool expected)
        => PunchTime.IsValid(input).Should().Be(expected);
}
