using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Entities;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Domain.Events;
using FluentAssertions;
using static ClaimsModule.Tests.Domain.TestData;

namespace ClaimsModule.Tests.Domain;

public class ClaimTransitionTests
{
    // ---- Draft -> Open (BR-ST-02)

    [Fact]
    public void Draft_to_open_succeeds_and_assigns_the_actor_as_handler()
    {
        var claim = Claim.Create("CLM-2026-0000001", ActivePolicy(), "X", ClaimSeverity.Standard, Now, Loss(), Now);
        claim.AddParty(Claimant(), Now);
        claim.AddRiskObject(ClaimRiskObject.Create(AssetType.Vehicle, "Truck"), Now);

        claim.TransitionTo(ClaimStatus.Open, UserRole.Handler, Now, Handler);

        claim.Status.Should().Be(ClaimStatus.Open);
        claim.AssignedHandlerId.Should().Be(Handler);
    }

    [Fact]
    public void Draft_to_open_is_blocked_without_a_claimant()
    {
        var claim = DraftClaim(withClaimant: false);

        var act = () => claim.TransitionTo(ClaimStatus.Open, UserRole.Handler, Now, Handler);

        act.Should().Throw<ClaimTransitionException>()
            .Which.BlockingConditions.Should().Contain(c => c.Contains("Claimant"));
        claim.Status.Should().Be(ClaimStatus.Draft);
    }

    [Fact]
    public void Draft_to_open_requires_loss_date_warning_to_be_acknowledged()
    {
        var expired = Policy.Create("POL-2023-000099", "Archived Corp", new DateOnly(2020, 1, 1), new DateOnly(2021, 12, 31),
            PolicyStatus.Expired, "Property");
        var claim = DraftClaim(expired);

        var blocked = () => claim.TransitionTo(ClaimStatus.Open, UserRole.Handler, Now, Handler);
        blocked.Should().Throw<ClaimTransitionException>().Which.BlockingConditions.Should().Contain(c => c.Contains("acknowledged"));

        claim.TransitionTo(ClaimStatus.Open, UserRole.Handler, Now, Handler, acknowledgeWarnings: true);

        claim.Status.Should().Be(ClaimStatus.Open);
        claim.ValidationIssues.Single(i => i.Code == ValidationIssueCodes.LossDateOutsidePolicy).IsAcknowledged.Should().BeTrue();
    }

    [Fact]
    public void Failed_transition_does_not_acknowledge_warnings_or_change_state()
    {
        var expired = Policy.Create("POL-2023-000099", "Archived Corp", new DateOnly(2020, 1, 1), new DateOnly(2021, 12, 31),
            PolicyStatus.Expired, "Property");
        var claim = DraftClaim(expired, withClaimant: false);

        var act = () => claim.TransitionTo(ClaimStatus.Open, UserRole.Handler, Now, Handler, acknowledgeWarnings: true);

        act.Should().Throw<ClaimTransitionException>();
        claim.ValidationIssues.Single(i => i.Code == ValidationIssueCodes.LossDateOutsidePolicy).IsAcknowledged.Should().BeFalse();
    }

    // ---- invalid transitions (BR-ST-01)

    [Fact]
    public void Invalid_transition_lists_valid_next_statuses()
    {
        var claim = DraftClaim();

        var act = () => claim.TransitionTo(ClaimStatus.Closed, UserRole.Manager, Now, Manager);

        var ex = act.Should().Throw<ClaimTransitionException>().Which;
        ex.Message.Should().Be("Transition from Draft to Closed is not permitted.");
        ex.ValidNextStatuses.Should().BeEquivalentTo(new[] { ClaimStatus.Open });
    }

    // ---- PendingPayment

    [Fact]
    public void Pending_payment_requires_an_approved_reserve()
    {
        var claim = OpenClaim();

        var act = () => claim.TransitionTo(ClaimStatus.PendingPayment, UserRole.Handler, Now, Handler);
        act.Should().Throw<ClaimTransitionException>();

        claim.SubmitReserve(ReserveComponentType.Indemnity, ReserveTransactionType.Add, 5_000m, "Initial", Now, Handler);
        claim.TransitionTo(ClaimStatus.PendingPayment, UserRole.Handler, Now, Handler);

        claim.Status.Should().Be(ClaimStatus.PendingPayment);
    }

    [Fact]
    public void Pending_approval_reserve_does_not_count_as_approved()
    {
        var claim = OpenClaim();
        claim.SubmitReserve(ReserveComponentType.Indemnity, ReserveTransactionType.Add, 50_000m, "Big loss", Now, Handler);

        var act = () => claim.TransitionTo(ClaimStatus.PendingPayment, UserRole.Handler, Now, Handler);

        act.Should().Throw<ClaimTransitionException>();
    }

    // ---- Closure (BR-ST-03, CC-01..04)

    [Fact]
    public void Closing_with_no_reserves_succeeds_and_stamps_closure()
    {
        var claim = OpenClaim();

        claim.TransitionTo(ClaimStatus.Closed, UserRole.Handler, Now, Handler, reason: "Settled");

        claim.Status.Should().Be(ClaimStatus.Closed);
        claim.ClosedAt.Should().Be(Now);
        claim.ClosureReason.Should().Be("Settled");
    }

    [Fact]
    public void Closing_is_blocked_while_a_reserve_is_pending_approval()
    {
        var claim = OpenClaim();
        claim.SubmitReserve(ReserveComponentType.Indemnity, ReserveTransactionType.Add, 20_000m, "Loss", Now, Handler);

        var act = () => claim.TransitionTo(ClaimStatus.Closed, UserRole.Supervisor, Now, Supervisor, closureJustification: "x");

        act.Should().Throw<ClaimTransitionException>().Which.BlockingConditions.Should().Contain(c => c.Contains("pending approval"));
    }

    [Fact]
    public void Closing_with_an_open_balance_needs_a_justification()
    {
        var claim = OpenClaim();
        claim.SubmitReserve(ReserveComponentType.Indemnity, ReserveTransactionType.Add, 5_000m, "Loss", Now, Handler);

        var blocked = () => claim.TransitionTo(ClaimStatus.Closed, UserRole.Handler, Now, Handler);
        blocked.Should().Throw<ClaimTransitionException>().Which.BlockingConditions.Should().Contain(c => c.Contains("justification"));

        claim.TransitionTo(ClaimStatus.Closed, UserRole.Handler, Now, Handler, closureJustification: "Claimant settled for less");

        claim.Status.Should().Be(ClaimStatus.Closed);
        claim.ClosureReason.Should().Be("Claimant settled for less");
    }

    [Fact]
    public void ClosureBlockers_can_be_previewed_without_changing_the_claim()
    {
        var claim = OpenClaim();
        claim.SubmitReserve(ReserveComponentType.Expense, ReserveTransactionType.Add, 1_000m, "Expert", Now, Handler);

        claim.ClosureBlockers().Should().HaveCount(1);
        claim.ClosureBlockers("because").Should().BeEmpty();
        claim.Status.Should().Be(ClaimStatus.Open);
    }

    // ---- Withdraw / reopen

    [Fact]
    public void Withdrawal_requires_a_reason()
    {
        var claim = OpenClaim();

        FluentActions.Invoking(() => claim.TransitionTo(ClaimStatus.Withdrawn, UserRole.Handler, Now, Handler))
            .Should().Throw<ClaimTransitionException>();

        claim.TransitionTo(ClaimStatus.Withdrawn, UserRole.Handler, Now, Handler, reason: "Claimant withdrew");
        claim.Status.Should().Be(ClaimStatus.Withdrawn);
        claim.ClosureReason.Should().Be("Claimant withdrew");
    }

    [Fact]
    public void Reopen_requires_supervisor_and_a_reason_and_lands_in_open()
    {
        var claim = OpenClaim();
        claim.TransitionTo(ClaimStatus.Closed, UserRole.Handler, Now, Handler, reason: "Done");
        claim.ClearDomainEvents();

        FluentActions.Invoking(() => claim.TransitionTo(ClaimStatus.Reopened, UserRole.Handler, Now, Handler, reason: "New info"))
            .Should().Throw<DomainException>().Which.Kind.Should().Be(DomainErrorKind.Authority);
        FluentActions.Invoking(() => claim.TransitionTo(ClaimStatus.Reopened, UserRole.Supervisor, Now, Supervisor))
            .Should().Throw<ClaimTransitionException>();

        claim.TransitionTo(ClaimStatus.Reopened, UserRole.Supervisor, Now, Supervisor, reason: "New evidence");

        claim.Status.Should().Be(ClaimStatus.Open);
        claim.ClosedAt.Should().BeNull();
        claim.DomainEvents.OfType<ClaimStatusChangedDomainEvent>().Select(e => (e.From, e.To)).Should().Equal(
            (ClaimStatus.Closed, ClaimStatus.Reopened), (ClaimStatus.Reopened, ClaimStatus.Open));
    }

    [Fact]
    public void Status_change_raises_an_event_with_the_reason()
    {
        var claim = OpenClaim();

        claim.TransitionTo(ClaimStatus.UnderInvestigation, UserRole.Handler, Now, Handler, reason: "Possible fraud");

        claim.DomainEvents.OfType<ClaimStatusChangedDomainEvent>().Should().ContainSingle()
            .Which.Should().Match<ClaimStatusChangedDomainEvent>(e =>
                e.From == ClaimStatus.Open && e.To == ClaimStatus.UnderInvestigation && e.Reason == "Possible fraud");
    }
}
