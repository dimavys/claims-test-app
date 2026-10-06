using ClaimsModule.Application.Common;
using ClaimsModule.Application.Features.Claims.Commands;
using ClaimsModule.Application.Features.Claims.Dtos;
using ClaimsModule.Application.Features.Claims.Queries;
using ClaimsModule.Application.Features.Reference;
using ClaimsModule.Application.Features.Reserves;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Entities;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Tests.Persistence;
using FluentAssertions;
using static ClaimsModule.Tests.Application.Users;

namespace ClaimsModule.Tests.Application;

[Collection(SqlServerCollection.Name)]
public sealed class ReserveFlowTests : IDisposable
{
    private readonly SqlServerFixture _sql;
    private readonly AppHarness _app;

    public ReserveFlowTests(SqlServerFixture sql)
    {
        _sql = sql;
        _app = new AppHarness(sql);
    }

    public void Dispose() => _app.Dispose();

    private async Task<Guid> NewOpenClaim()
    {
        Skip.IfNot(_sql.Available, "SQL Server not available");
        var policy = (await _app.Send(new SearchPoliciesQuery("POL-2024-001001"))).Single().Id;
        var created = await _app.Send(new CreateClaimCommand(
            policy, DateTimeOffset.UtcNow.AddDays(-1), "Warehouse fire damaged stored inventory", "COL-FIRE",
            Parties: new[] { new PartyInput(PartyRole.Claimant, PartyType.Company, CompanyName: "Meridian Transport LLC") },
            RiskObjects: new[] { new RiskObjectInput(AssetType.Property, "Warehouse 4") }));
        await _app.Send(new TransitionClaimStatusCommand(created.Id, ClaimStatus.Open));
        return created.Id;
    }

    private Task<ReserveSubmissionDto> Submit(Guid claim, decimal amount, TestUser by,
        ReserveComponentType component = ReserveComponentType.Indemnity, ReserveTransactionType type = ReserveTransactionType.Add) =>
        _app.Send(new SubmitReserveCommand(claim, component, amount, "Test reserve", type), by);

    private async Task<List<string>> AuditTypes(Guid claim) =>
        (await _app.Send(new GetClaimAuditQuery(claim, 1, 100))).Items.Select(a => a.EventType).ToList();

    // ---- authority tiers

    [SkippableFact]
    public async Task Auto_approved_reserve_updates_the_balance_and_queues_a_job_only_after_commit()
    {
        var claim = await NewOpenClaim();
        var queued = _app.Scheduler.Enqueued.Count;

        var result = await Submit(claim, 10_000m, Handler);

        result.Transaction.ApprovalStatus.Should().Be(ReserveApprovalStatus.AutoApproved);
        result.Transaction.SubmittedByName.Should().Be(Handler.Name);
        result.RequiresApproval.Should().BeFalse();
        _app.Scheduler.Enqueued.Count.Should().Be(queued + 1);
        (await _app.Send(new GetReservesQuery(claim))).Summary.TotalReserves.Should().Be(10_000m);
    }

    [SkippableFact]
    public async Task Large_reserve_waits_then_a_supervisor_approves_and_the_job_is_queued_once()
    {
        var claim = await NewOpenClaim();
        var queued = _app.Scheduler.Enqueued.Count;

        var pending = await Submit(claim, 50_000m, Handler);
        pending.Transaction.ApprovalStatus.Should().Be(ReserveApprovalStatus.PendingApproval);
        pending.RequiredAuthority.Should().Be("Supervisor approval required");
        _app.Scheduler.Enqueued.Count.Should().Be(queued, "no GL posting before approval");

        var reserves = await _app.Send(new GetReservesQuery(claim));
        reserves.Summary.TotalReserves.Should().Be(0m);
        reserves.Summary.TotalPending.Should().Be(50_000m);

        var approved = await _app.Send(new ApproveReserveCommand(claim, pending.Transaction.Id), Supervisor);

        approved.ApprovalStatus.Should().Be(ReserveApprovalStatus.Approved);
        approved.ApprovedByName.Should().Be(Supervisor.Name);
        approved.NewBalance.Should().Be(50_000m);
        _app.Scheduler.Enqueued.Count.Should().Be(queued + 1);
        (await _app.Send(new GetReservesQuery(claim))).Summary.TotalReserves.Should().Be(50_000m);
        (await AuditTypes(claim)).Should().Contain(new[] { AuditEventTypes.ReserveCreated, AuditEventTypes.ReserveApproved });
    }

    [SkippableFact]
    public async Task Handler_cannot_approve_and_nothing_changes()
    {
        var claim = await NewOpenClaim();
        var pending = await Submit(claim, 20_000m, Handler);
        var queued = _app.Scheduler.Enqueued.Count;
        var auditBefore = (await AuditTypes(claim)).Count;

        await FluentActions.Invoking(() => _app.Send(new ApproveReserveCommand(claim, pending.Transaction.Id), Handler2))
            .Should().ThrowAsync<DomainException>().Where(e => e.Kind == DomainErrorKind.Authority);

        _app.Scheduler.Enqueued.Count.Should().Be(queued);
        (await AuditTypes(claim)).Count.Should().Be(auditBefore, "a failed command rolls back its audit entries too");
        (await _app.Send(new GetReservesQuery(claim))).Transactions.Single().ApprovalStatus.Should().Be(ReserveApprovalStatus.PendingApproval);
    }

    [SkippableFact]
    public async Task Supervisor_cannot_approve_their_own_reserve()
    {
        var claim = await NewOpenClaim();
        var pending = await Submit(claim, 20_000m, Supervisor);

        await FluentActions.Invoking(() => _app.Send(new ApproveReserveCommand(claim, pending.Transaction.Id), Supervisor))
            .Should().ThrowAsync<DomainException>().WithMessage("Self-approval is not permitted.");

        await _app.Send(new ApproveReserveCommand(claim, pending.Transaction.Id), Manager); // a different approver can
    }

    [SkippableFact]
    public async Task Over_100k_needs_a_manager()
    {
        var claim = await NewOpenClaim();
        var pending = await Submit(claim, 150_000m, Handler);

        await FluentActions.Invoking(() => _app.Send(new ApproveReserveCommand(claim, pending.Transaction.Id), Supervisor))
            .Should().ThrowAsync<DomainException>().WithMessage("Your role does not have authority*");

        var approved = await _app.Send(new ApproveReserveCommand(claim, pending.Transaction.Id), Manager);
        approved.ApprovalStatus.Should().Be(ReserveApprovalStatus.Approved);
    }

    [SkippableFact]
    public async Task Reject_records_the_reason_keeps_history_and_allows_a_resubmission()
    {
        var claim = await NewOpenClaim();
        var first = await Submit(claim, 20_000m, Handler);

        await FluentActions.Invoking(() => _app.Send(new RejectReserveCommand(claim, first.Transaction.Id, " "), Supervisor))
            .Should().ThrowAsync<RequestValidationException>();
        var rejected = await _app.Send(new RejectReserveCommand(claim, first.Transaction.Id, "Estimate too high"), Supervisor);
        var second = await Submit(claim, 15_000m, Handler);

        rejected.ApprovalStatus.Should().Be(ReserveApprovalStatus.Rejected);
        rejected.RejectionReason.Should().Be("Estimate too high");
        second.Transaction.ChangeSequence.Should().Be(2);
        var reserves = await _app.Send(new GetReservesQuery(claim));
        reserves.Transactions.Should().HaveCount(2).And.Contain(t => t.Id == first.Transaction.Id && t.ApprovalStatus == ReserveApprovalStatus.Rejected);
        (await _app.Send(new GetClaimAuditQuery(claim, 1, 100))).Items
            .Single(a => a.EventType == AuditEventTypes.ReserveRejected).OldValue.Should().Contain("Estimate too high");
    }

    [SkippableFact]
    public async Task Submitter_can_retract_a_pending_reserve_but_others_cannot()
    {
        var claim = await NewOpenClaim();
        var pending = await Submit(claim, 20_000m, Handler);

        await FluentActions.Invoking(() => _app.Send(new RetractReserveCommand(claim, pending.Transaction.Id), Handler2))
            .Should().ThrowAsync<DomainException>().Where(e => e.Kind == DomainErrorKind.Authority);

        var retracted = await _app.Send(new RetractReserveCommand(claim, pending.Transaction.Id), Handler);

        retracted.ApprovalStatus.Should().Be(ReserveApprovalStatus.Cancelled);
        (await AuditTypes(claim)).Should().Contain(AuditEventTypes.ReserveRetracted);
        await FluentActions.Invoking(() => _app.Send(new ApproveReserveCommand(claim, pending.Transaction.Id), Supervisor))
            .Should().ThrowAsync<DomainException>().Where(e => e.Kind == DomainErrorKind.Conflict);
    }

    // ---- amount rules & components

    [SkippableFact]
    public async Task Zero_and_negative_amounts_are_rejected_except_for_subrogation()
    {
        var claim = await NewOpenClaim();

        await FluentActions.Invoking(() => Submit(claim, 0m, Handler)).Should().ThrowAsync<RequestValidationException>();
        await FluentActions.Invoking(() => Submit(claim, -5m, Handler)).Should().ThrowAsync<RequestValidationException>()
            .WithMessage("*validation errors*");
        await FluentActions.Invoking(() => Submit(claim, 0m, Handler, ReserveComponentType.SubrogationRecoverable)).Should().ThrowAsync<RequestValidationException>();

        var subro = await Submit(claim, -8_000m, Handler, ReserveComponentType.SubrogationRecoverable);
        subro.Transaction.NewBalance.Should().Be(-8_000m);
    }

    [SkippableFact]
    public async Task Adjust_uses_the_component_id_and_reverse_cannot_go_below_zero()
    {
        var claim = await NewOpenClaim();
        var first = await Submit(claim, 8_000m, Handler);

        var adjusted = await _app.Send(new AdjustReserveCommand(claim, first.Transaction.ReserveComponentId, 1_500m, "More damage found"), Handler);
        adjusted.Transaction.TransactionType.Should().Be(ReserveTransactionType.Adjust);
        adjusted.Transaction.NewBalance.Should().Be(9_500m);

        await FluentActions.Invoking(() => Submit(claim, 20_000m, Handler, type: ReserveTransactionType.Reverse))
            .Should().ThrowAsync<DomainException>().WithMessage("*cannot go negative*");
        await FluentActions.Invoking(() => _app.Send(new AdjustReserveCommand(claim, Guid.NewGuid(), 10m, "x"), Handler))
            .Should().ThrowAsync<NotFoundException>();
    }

    [SkippableFact]
    public async Task Reserve_summary_tracks_components_and_pending_amounts_separately()
    {
        var claim = await NewOpenClaim();
        await Submit(claim, 5_000m, Handler);
        await Submit(claim, 2_000m, Handler, ReserveComponentType.Expense);
        await Submit(claim, 30_000m, Handler, ReserveComponentType.ALAE);

        var summary = (await _app.Send(new GetReservesQuery(claim))).Summary;

        summary.Components.Should().HaveCount(3);
        summary.TotalReserves.Should().Be(7_000m);
        summary.TotalPending.Should().Be(30_000m);
        summary.Components.Single(c => c.Component == ReserveComponentType.ALAE).PendingAmount.Should().Be(30_000m);
    }

    [SkippableFact]
    public async Task Claim_detail_embeds_the_reserve_summary_and_recent_audit()
    {
        var claim = await NewOpenClaim();
        await Submit(claim, 5_000m, Handler);

        var detail = await _app.Send(new GetClaimDetailQuery(claim));

        detail.ReserveSummary.TotalReserves.Should().Be(5_000m);
        detail.RecentAudit.Should().NotBeEmpty().And.HaveCountLessThanOrEqualTo(10);
        detail.RecentAudit.Select(a => a.CreatedAt).Should().BeInDescendingOrder();
    }

    // ---- aggregate cap & override

    [SkippableFact]
    public async Task Aggregate_limit_blocks_approval_until_a_manager_sets_the_override()
    {
        var claim = await NewOpenClaim();
        var big = await Submit(claim, 9_999_000m, Handler);
        await _app.Send(new ApproveReserveCommand(claim, big.Transaction.Id), Manager);

        var small = await Submit(claim, 5_000m, Handler);
        small.Warnings.Should().ContainSingle(w => w.Contains("Manager override required"));
        small.Transaction.ApprovalStatus.Should().Be(ReserveApprovalStatus.PendingApproval);

        await FluentActions.Invoking(() => _app.Send(new ApproveReserveCommand(claim, small.Transaction.Id), Supervisor))
            .Should().ThrowAsync<DomainException>().WithMessage("*override required*");
        await FluentActions.Invoking(() => _app.Send(new SetManagerOverrideCommand(claim), Supervisor))
            .Should().ThrowAsync<DomainException>().Where(e => e.Kind == DomainErrorKind.Authority);

        await _app.Send(new SetManagerOverrideCommand(claim), Manager);
        await _app.Send(new ApproveReserveCommand(claim, small.Transaction.Id), Supervisor);

        var detail = await _app.Send(new GetClaimDetailQuery(claim));
        detail.ManagerOverride.Should().BeTrue();
        detail.ReserveSummary.TotalReserves.Should().Be(10_004_000m);
    }

    // ---- lifecycle interaction

    [SkippableFact]
    public async Task Pending_payment_needs_an_approved_reserve_and_closing_needs_pending_ones_resolved()
    {
        var claim = await NewOpenClaim();
        await FluentActions.Invoking(() => _app.Send(new TransitionClaimStatusCommand(claim, ClaimStatus.PendingPayment)))
            .Should().ThrowAsync<ClaimTransitionException>();

        var pending = await Submit(claim, 20_000m, Handler);
        await FluentActions.Invoking(() => _app.Send(new TransitionClaimStatusCommand(claim, ClaimStatus.Closed, ClosureJustification: "x"), Supervisor))
            .Should().ThrowAsync<ClaimTransitionException>().Where(e => e.BlockingConditions.Any(c => c.Contains("pending approval")));

        await _app.Send(new ApproveReserveCommand(claim, pending.Transaction.Id), Supervisor);
        var result = await _app.Send(new TransitionClaimStatusCommand(claim, ClaimStatus.PendingPayment));
        result.Status.Should().Be(ClaimStatus.PendingPayment);
    }

    [SkippableFact]
    public async Task Reserves_are_blocked_without_a_policy()
    {
        Skip.IfNot(_sql.Available, "SQL Server not available");
        var created = await _app.Send(new CreateClaimCommand(
            null, DateTimeOffset.UtcNow.AddDays(-1), "Unknown policy claim for testing blocks", "COL-OTHER",
            Parties: new[] { new PartyInput(PartyRole.Claimant, PartyType.Person, "No", "Policy") }));

        await FluentActions.Invoking(() => Submit(created.Id, 1_000m, Handler))
            .Should().ThrowAsync<DomainException>().WithMessage("No policy linked*");
    }

    // ---- concurrency

    [SkippableFact]
    public async Task Two_approvers_racing_cannot_both_win()
    {
        var claim = await NewOpenClaim();
        var pending = await Submit(claim, 20_000m, Handler);

        var attempts = await Task.WhenAll(
            Try(() => _app.Send(new ApproveReserveCommand(claim, pending.Transaction.Id), Supervisor)),
            Try(() => _app.Send(new ApproveReserveCommand(claim, pending.Transaction.Id), Manager)));

        attempts.Count(ok => ok).Should().Be(1);
        (await _app.Send(new GetReservesQuery(claim))).Summary.TotalReserves.Should().Be(20_000m, "the balance is applied exactly once");

        static async Task<bool> Try(Func<Task<ReserveTransactionDto>> action)
        {
            try { await action(); return true; }
            catch (Exception) { return false; }
        }
    }
}
