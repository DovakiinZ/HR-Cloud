using FluentAssertions;
using HR.Domain.Engines.Finance.StateMachine;
using HR.Domain.Enums;
using Xunit;

namespace HR.Domain.Finance.Tests;

/// <summary>SP6 Task 1 — a posted run (Completed/Locked) can be Voided; Voided is terminal and, crucially,
/// NOT immutable so it releases its hold on the period for an amend run.</summary>
public class VoidStateMachineTests
{
    [Fact]
    public void Completed_can_transition_to_Voided()
        => PayrollRunStateMachine.CanTransition(PayrollRunState.Completed, PayrollRunState.Voided).Should().BeTrue();

    [Fact]
    public void Locked_can_transition_to_Voided()
        => PayrollRunStateMachine.CanTransition(PayrollRunState.Locked, PayrollRunState.Voided).Should().BeTrue();

    [Fact]
    public void Voided_is_terminal()
    {
        PayrollRunStateMachine.IsTerminal(PayrollRunState.Voided).Should().BeTrue();
        PayrollRunStateMachine.NextStates(PayrollRunState.Voided).Should().BeEmpty();
    }

    [Fact]
    public void Voided_is_not_immutable_so_the_period_reopens_for_an_amendment()
        => PayrollRunStateMachine.IsImmutable(PayrollRunState.Voided).Should().BeFalse();

    [Fact]
    public void A_draft_run_cannot_be_voided()
        => PayrollRunStateMachine.CanTransition(PayrollRunState.Draft, PayrollRunState.Voided).Should().BeFalse();
}
