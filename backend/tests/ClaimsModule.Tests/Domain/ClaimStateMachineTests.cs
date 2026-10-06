using ClaimsModule.Domain.Enums;
using ClaimsModule.Domain.Rules;
using FluentAssertions;

namespace ClaimsModule.Tests.Domain;

public class ClaimStateMachineTests
{
    [Theory]
    [InlineData(ClaimStatus.Draft, ClaimStatus.Open)]
    [InlineData(ClaimStatus.Open, ClaimStatus.UnderInvestigation)]
    [InlineData(ClaimStatus.Open, ClaimStatus.PendingPayment)]
    [InlineData(ClaimStatus.Open, ClaimStatus.Closed)]
    [InlineData(ClaimStatus.Open, ClaimStatus.Withdrawn)]
    [InlineData(ClaimStatus.UnderInvestigation, ClaimStatus.Open)]
    [InlineData(ClaimStatus.UnderInvestigation, ClaimStatus.PendingPayment)]
    [InlineData(ClaimStatus.UnderInvestigation, ClaimStatus.Closed)]
    [InlineData(ClaimStatus.UnderInvestigation, ClaimStatus.Withdrawn)]
    [InlineData(ClaimStatus.PendingPayment, ClaimStatus.Closed)]
    [InlineData(ClaimStatus.Closed, ClaimStatus.Reopened)]
    [InlineData(ClaimStatus.Reopened, ClaimStatus.Open)]
    public void Listed_transitions_are_allowed(ClaimStatus from, ClaimStatus to) =>
        ClaimStateMachine.IsAllowed(from, to).Should().BeTrue();

    [Fact]
    public void Exactly_twelve_transitions_exist() => ClaimStateMachine.Rules.Should().HaveCount(12);

    [Theory]
    [InlineData(ClaimStatus.Draft, ClaimStatus.Closed)]
    [InlineData(ClaimStatus.Draft, ClaimStatus.PendingPayment)]
    [InlineData(ClaimStatus.PendingPayment, ClaimStatus.Open)]
    [InlineData(ClaimStatus.Closed, ClaimStatus.Open)]
    [InlineData(ClaimStatus.Withdrawn, ClaimStatus.Open)]
    [InlineData(ClaimStatus.Open, ClaimStatus.Draft)]
    public void Unlisted_transitions_are_rejected(ClaimStatus from, ClaimStatus to) =>
        ClaimStateMachine.IsAllowed(from, to).Should().BeFalse();

    [Fact]
    public void Withdrawn_is_terminal() => ClaimStateMachine.ValidNextStatuses(ClaimStatus.Withdrawn).Should().BeEmpty();

    [Fact]
    public void Only_reopening_needs_a_supervisor() =>
        ClaimStateMachine.Rules.Where(r => r.MinimumRole == UserRole.Supervisor)
            .Should().ContainSingle().Which.To.Should().Be(ClaimStatus.Reopened);
}
