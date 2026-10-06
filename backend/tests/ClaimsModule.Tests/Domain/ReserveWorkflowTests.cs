using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Entities;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Domain.Events;
using FluentAssertions;
using static ClaimsModule.Tests.Domain.TestData;

namespace ClaimsModule.Tests.Domain;

public class ReserveWorkflowTests
{
    private static ReserveSubmissionResult Submit(Claim claim, decimal amount,
        ReserveComponentType component = ReserveComponentType.Indemnity,
        ReserveTransactionType type = ReserveTransactionType.Add, Guid? by = null) =>
        claim.SubmitReserve(component, type, amount, "Test reserve", Now, by ?? Handler);

    // ---- authority tiers (BR-R-02)

    [Fact]
    public void Amount_up_to_10k_is_auto_approved_and_applied_immediately()
    {
        var claim = OpenClaim();

        var result = Submit(claim, 10_000m);

        result.Transaction.ApprovalStatus.Should().Be(ReserveApprovalStatus.AutoApproved);
        claim.TotalReserves.Should().Be(10_000m);
        result.Transaction.PreviousBalance.Should().Be(0m);
        result.Transaction.NewBalance.Should().Be(10_000m);
        claim.DomainEvents.OfType<ReserveApprovedDomainEvent>().Should().ContainSingle(e => e.AutoApproved);
    }

    [Fact]
    public void Amount_over_10k_waits_for_approval_without_touching_the_balance()
    {
        var claim = OpenClaim();

        var result = Submit(claim, 10_000.01m);

        result.Transaction.ApprovalStatus.Should().Be(ReserveApprovalStatus.PendingApproval);
        claim.TotalReserves.Should().Be(0m);
        claim.ReserveComponents.Single().PendingAmount.Should().Be(10_000.01m);
        claim.DomainEvents.OfType<ReserveApprovedDomainEvent>().Should().BeEmpty();
    }

    [Fact]
    public void Submitter_with_a_high_role_still_goes_through_approval_for_large_amounts()
    {
        var claim = OpenClaim();

        var result = Submit(claim, 50_000m, by: Manager);

        result.Transaction.ApprovalStatus.Should().Be(ReserveApprovalStatus.PendingApproval);
    }

    // ---- approval

    [Fact]
    public void Supervisor_approves_a_mid_sized_reserve()
    {
        var claim = OpenClaim();
        var txn = Submit(claim, 50_000m).Transaction;

        claim.ApproveReserve(txn.Id, Supervisor, UserRole.Supervisor, Now);

        txn.ApprovalStatus.Should().Be(ReserveApprovalStatus.Approved);
        txn.ApprovedByUserId.Should().Be(Supervisor);
        claim.TotalReserves.Should().Be(50_000m);
        claim.DomainEvents.OfType<ReserveApprovedDomainEvent>().Should().ContainSingle(e => !e.AutoApproved);
    }

    [Fact]
    public void Supervisor_cannot_approve_above_100k_but_manager_can()
    {
        var claim = OpenClaim();
        var txn = Submit(claim, 150_000m).Transaction;

        FluentActions.Invoking(() => claim.ApproveReserve(txn.Id, Supervisor, UserRole.Supervisor, Now))
            .Should().Throw<DomainException>().Which.Kind.Should().Be(DomainErrorKind.Authority);

        claim.ApproveReserve(txn.Id, Manager, UserRole.Manager, Now);
        claim.TotalReserves.Should().Be(150_000m);
    }

    [Fact]
    public void Self_approval_is_rejected_even_when_the_role_allows_the_amount()
    {
        var claim = OpenClaim();
        var txn = Submit(claim, 50_000m, by: Supervisor).Transaction;

        var act = () => claim.ApproveReserve(txn.Id, Supervisor, UserRole.Supervisor, Now);

        act.Should().Throw<DomainException>().WithMessage("Self-approval is not permitted.")
            .Which.Kind.Should().Be(DomainErrorKind.Validation);
    }

    [Fact]
    public void Approval_balances_are_computed_at_approval_time()
    {
        var claim = OpenClaim();
        var pending = Submit(claim, 20_000m).Transaction;
        Submit(claim, 5_000m);

        claim.ApproveReserve(pending.Id, Supervisor, UserRole.Supervisor, Now);

        pending.PreviousBalance.Should().Be(5_000m);
        pending.NewBalance.Should().Be(25_000m);
    }

    [Fact]
    public void An_already_decided_transaction_cannot_be_approved_again()
    {
        var claim = OpenClaim();
        var txn = Submit(claim, 20_000m).Transaction;
        claim.ApproveReserve(txn.Id, Supervisor, UserRole.Supervisor, Now);

        FluentActions.Invoking(() => claim.ApproveReserve(txn.Id, Manager, UserRole.Manager, Now))
            .Should().Throw<DomainException>().Which.Kind.Should().Be(DomainErrorKind.Conflict);
    }

    [Fact]
    public void Unknown_transaction_is_not_found()
    {
        FluentActions.Invoking(() => OpenClaim().ApproveReserve(Guid.NewGuid(), Supervisor, UserRole.Supervisor, Now))
            .Should().Throw<DomainException>().Which.Kind.Should().Be(DomainErrorKind.NotFound);
    }

    // ---- reject / retract (BR-R-04)

    [Fact]
    public void Rejection_keeps_the_record_records_the_reason_and_leaves_the_balance()
    {
        var claim = OpenClaim();
        var txn = Submit(claim, 20_000m).Transaction;

        claim.RejectReserve(txn.Id, Supervisor, UserRole.Supervisor, "Estimate too high", Now);

        txn.ApprovalStatus.Should().Be(ReserveApprovalStatus.Rejected);
        txn.RejectionReason.Should().Be("Estimate too high");
        txn.PostingStatus.Should().Be(ReservePostingStatus.Cancelled);
        claim.TotalReserves.Should().Be(0m);
        claim.ReserveComponents.Single().History.Should().Contain(txn);
    }

    [Fact]
    public void Rejection_needs_a_reason()
    {
        var claim = OpenClaim();
        var txn = Submit(claim, 20_000m).Transaction;

        FluentActions.Invoking(() => claim.RejectReserve(txn.Id, Supervisor, UserRole.Supervisor, " ", Now))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Resubmission_after_rejection_creates_a_new_record_with_the_next_sequence()
    {
        var claim = OpenClaim();
        var first = Submit(claim, 20_000m).Transaction;
        claim.RejectReserve(first.Id, Supervisor, UserRole.Supervisor, "Too high", Now);

        var second = Submit(claim, 15_000m).Transaction;

        second.Id.Should().NotBe(first.Id);
        second.ChangeSequence.Should().Be(2);
        claim.ReserveComponents.Single().History.Should().HaveCount(2);
    }

    [Fact]
    public void Only_the_submitter_can_retract_and_only_while_pending()
    {
        var claim = OpenClaim();
        var txn = Submit(claim, 20_000m).Transaction;

        FluentActions.Invoking(() => claim.RetractReserve(txn.Id, Supervisor))
            .Should().Throw<DomainException>().Which.Kind.Should().Be(DomainErrorKind.Authority);

        claim.RetractReserve(txn.Id, Handler);
        txn.ApprovalStatus.Should().Be(ReserveApprovalStatus.Cancelled);

        FluentActions.Invoking(() => claim.RetractReserve(txn.Id, Handler))
            .Should().Throw<DomainException>().Which.Kind.Should().Be(DomainErrorKind.Conflict);
    }

    // ---- amount rules (BR-R-01)

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_subrogation_reserves_must_be_positive(double amount)
    {
        FluentActions.Invoking(() => Submit(OpenClaim(), (decimal)amount))
            .Should().Throw<DomainException>().WithMessage("Reserve amount must be greater than zero.");
    }

    [Fact]
    public void Subrogation_may_be_negative_and_the_balance_goes_negative()
    {
        var claim = OpenClaim();
        Submit(claim, 25_000m).Transaction.Should().NotBeNull();

        var result = Submit(claim, -8_000m, ReserveComponentType.SubrogationRecoverable);

        result.Transaction.ApprovalStatus.Should().Be(ReserveApprovalStatus.AutoApproved);
        claim.ReserveComponents.Single(c => c.Component == ReserveComponentType.SubrogationRecoverable).CurrentAmount.Should().Be(-8_000m);
    }

    [Fact]
    public void Zero_subrogation_is_rejected()
    {
        FluentActions.Invoking(() => Submit(OpenClaim(), 0m, ReserveComponentType.SubrogationRecoverable))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void Reverse_reduces_the_balance_but_not_below_zero()
    {
        var claim = OpenClaim();
        Submit(claim, 8_000m);

        Submit(claim, 3_000m, type: ReserveTransactionType.Reverse);
        claim.TotalReserves.Should().Be(5_000m);

        FluentActions.Invoking(() => Submit(claim, 6_000m, type: ReserveTransactionType.Reverse))
            .Should().Throw<DomainException>().WithMessage("*cannot go negative*");
    }

    [Fact]
    public void Components_are_tracked_independently()
    {
        var claim = OpenClaim();
        Submit(claim, 25_000m);
        Submit(claim, 5_000m, ReserveComponentType.Expense);

        claim.ReserveComponents.Should().HaveCount(2);
        claim.ReserveComponents.Single(c => c.Component == ReserveComponentType.Expense).CurrentAmount.Should().Be(5_000m);
    }

    // ---- claim-level preconditions (BR-C-06)

    [Fact]
    public void Reserves_are_blocked_until_a_policy_is_linked()
    {
        var claim = Claim.Create("CLM-2026-0000009", null, "Unknown", ClaimSeverity.Minor, Now, Loss(), Now);

        FluentActions.Invoking(() => claim.SubmitReserve(ReserveComponentType.Indemnity, ReserveTransactionType.Add, 100m, "x", Now))
            .Should().Throw<DomainException>().WithMessage("No policy linked*");
    }

    [Fact]
    public void Reserves_are_blocked_on_closed_claims()
    {
        var claim = OpenClaim();
        claim.TransitionTo(ClaimStatus.Closed, UserRole.Handler, Now, Handler, reason: "done");

        FluentActions.Invoking(() => Submit(claim, 100m)).Should().Throw<DomainException>();
    }

    // ---- aggregate limit (BR-R-05)

    [Fact]
    public void Exceeding_the_aggregate_limit_forces_approval_and_needs_a_manager_override()
    {
        var claim = OpenClaim();
        var big = Submit(claim, 9_999_000m).Transaction;
        claim.ApproveReserve(big.Id, Manager, UserRole.Manager, Now);

        var small = Submit(claim, 5_000m);

        small.Warnings.Should().ContainSingle(w => w.Contains("Manager override required"));
        small.Transaction.ApprovalStatus.Should().Be(ReserveApprovalStatus.PendingApproval);
        claim.ValidationIssues.Should().Contain(i => i.Code == ValidationIssueCodes.AggregateReserveLimit);

        FluentActions.Invoking(() => claim.ApproveReserve(small.Transaction.Id, Supervisor, UserRole.Supervisor, Now))
            .Should().Throw<DomainException>().WithMessage("*override required*");

        claim.SetManagerOverride(UserRole.Manager);
        claim.ApproveReserve(small.Transaction.Id, Supervisor, UserRole.Supervisor, Now);
        claim.TotalReserves.Should().Be(10_004_000m);
    }

    [Fact]
    public void Only_a_manager_can_set_the_override()
    {
        FluentActions.Invoking(() => OpenClaim().SetManagerOverride(UserRole.Supervisor))
            .Should().Throw<DomainException>().Which.Kind.Should().Be(DomainErrorKind.Authority);
    }

    // ---- GL posting idempotency (BR-R-06)

    [Fact]
    public void Idempotency_key_embeds_component_and_sequence()
    {
        var claim = OpenClaim();
        var txn = Submit(claim, 1_000m).Transaction;

        txn.IdempotencyKey.Should().Be($"Reserve:{txn.ReserveComponentId}:Change:1");
    }

    [Fact]
    public void Posting_twice_marks_posted_once()
    {
        var claim = OpenClaim();
        var txn = Submit(claim, 1_000m).Transaction;

        txn.TryMarkPosted("job-1").Should().BeTrue();
        txn.TryMarkPosted("job-2").Should().BeFalse();

        txn.PostingStatus.Should().Be(ReservePostingStatus.Posted);
        txn.PostingJobId.Should().Be("job-1");
    }

    [Fact]
    public void Unapproved_transactions_cannot_be_posted()
    {
        var claim = OpenClaim();
        var txn = Submit(claim, 20_000m).Transaction;

        FluentActions.Invoking(() => txn.TryMarkPosted("job")).Should().Throw<DomainException>();
    }

    [Fact]
    public void A_posted_transaction_is_not_downgraded_to_failed()
    {
        var claim = OpenClaim();
        var txn = Submit(claim, 1_000m).Transaction;
        txn.TryMarkPosted("job");

        txn.MarkPostingFailed();

        txn.PostingStatus.Should().Be(ReservePostingStatus.Posted);
    }
}

public class GlRetryTests
{
    [Fact]
    public void Only_failed_postings_can_be_retried_and_raise_an_event()
    {
        var claim = OpenClaim();
        var txn = claim.SubmitReserve(ReserveComponentType.Indemnity, ReserveTransactionType.Add, 1_000m, "r", Now, Handler).Transaction;
        claim.ClearDomainEvents();

        FluentActions.Invoking(() => claim.RetryGlPosting(txn.Id)).Should().Throw<DomainException>();

        txn.MarkPostingFailed();
        claim.RetryGlPosting(txn.Id);

        txn.PostingStatus.Should().Be(ReservePostingStatus.Pending);
        claim.DomainEvents.OfType<GlPostingRetryRequestedDomainEvent>().Should().ContainSingle()
            .Which.IdempotencyKey.Should().Be(txn.IdempotencyKey);
    }
}
