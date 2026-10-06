using ClaimsModule.Application.Features.Claims.Commands;
using ClaimsModule.Application.Features.Claims.Dtos;
using ClaimsModule.Application.Features.Claims.Queries;
using ClaimsModule.Application.Features.Reference;
using ClaimsModule.Application.Features.Reserves;
using ClaimsModule.Domain.Entities;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Infrastructure.Jobs;
using ClaimsModule.Tests.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using static ClaimsModule.Tests.Application.Users;

namespace ClaimsModule.Tests.Application;

public class GlJournalTests
{
    [Fact]
    public void An_increase_books_the_documented_entry()
    {
        var j = GlJournal.For("Indemnity", 5_000m);

        j.Description.Should().Be("DR Change in Outstanding Reserves / CR Outstanding Loss Reserves");
        j.Amount.Should().Be(5_000m);
    }

    [Fact]
    public void A_decrease_books_the_mirror_entry_with_a_positive_amount()
    {
        var j = GlJournal.For("SubrogationRecoverable", -8_000m);

        j.Description.Should().Be("DR Outstanding Loss Reserves / CR Change in Outstanding Reserves");
        j.Amount.Should().Be(8_000m);
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class JobTests : IDisposable
{
    private readonly SqlServerFixture _sql;
    private readonly AppHarness _app;

    public JobTests(SqlServerFixture sql)
    {
        _sql = sql;
        _app = new AppHarness(sql);
    }

    public void Dispose() => _app.Dispose();

    private PostGlReserveChangeJob GlJob() => _app.Root.GetRequiredService<PostGlReserveChangeJob>();
    private SlaMonitoringJob SlaJob() => _app.Root.GetRequiredService<SlaMonitoringJob>();

    private async Task<(Guid ClaimId, ReserveTransactionDto Txn)> ClaimWithReserve(decimal amount, ReserveComponentType component = ReserveComponentType.Indemnity)
    {
        Skip.IfNot(_sql.Available, "SQL Server not available");
        var policy = (await _app.Send(new SearchPoliciesQuery("POL-2024-001001"))).Single().Id;
        var created = await _app.Send(new CreateClaimCommand(
            policy, DateTimeOffset.UtcNow.AddDays(-1), "Hailstorm damaged the roof of the depot", "COL-WIND",
            Parties: new[] { new PartyInput(PartyRole.Claimant, PartyType.Company, CompanyName: "Meridian Transport LLC") },
            RiskObjects: new[] { new RiskObjectInput(AssetType.Property, "Depot roof") }));
        await _app.Send(new TransitionClaimStatusCommand(created.Id, ClaimStatus.Open));
        var submission = await _app.Send(new SubmitReserveCommand(created.Id, component, amount, "Initial", ReserveTransactionType.Add), Handler);
        return (created.Id, submission.Transaction);
    }

    private static GlPostingRequest Request(Guid claimId, ReserveTransactionDto txn) =>
        new(SeedDefault, claimId, txn.Id, txn.IdempotencyKey);

    private static readonly Guid SeedDefault = ClaimsModule.Persistence.Seed.SeedData.DefaultOrganisationId;

    private async Task<List<AuditEntryDto>> Audit(Guid claimId, string eventType) =>
        (await _app.Send(new GetClaimAuditQuery(claimId, 1, 100))).Items.Where(a => a.EventType == eventType).ToList();

    private async Task<ReserveTransactionDto> Txn(Guid claimId, Guid txnId) =>
        (await _app.Send(new GetReservesQuery(claimId))).Transactions.Single(t => t.Id == txnId);

    // ------------------------------------------------------------------ GL posting

    [SkippableFact]
    public async Task Posting_marks_the_transaction_posted_and_writes_one_structured_audit_entry()
    {
        var (claim, txn) = await ClaimWithReserve(5_000m);
        txn.PostingStatus.Should().Be(ReservePostingStatus.Pending);

        var outcome = await GlJob().RunAsync(Request(claim, txn), "hf-42", isFinalAttempt: false, default);

        outcome.Should().Be(GlPostingOutcome.Posted);
        var after = await Txn(claim, txn.Id);
        after.PostingStatus.Should().Be(ReservePostingStatus.Posted);
        after.PostingJobId.Should().Be("hf-42");

        var entry = (await Audit(claim, AuditEventTypes.GlPostingSimulated)).Should().ContainSingle().Subject;
        entry.Description.Should().Be("Simulated GL posting: DR Change in Outstanding Reserves / CR Outstanding Loss Reserves, Amount = 5,000.00");
        entry.NewValue.Should().Contain("\"amount\":5000").And.Contain(txn.IdempotencyKey);
        entry.RelatedEntityId.Should().Be(txn.Id);
        entry.CreatedByName.Should().Be("System");
    }

    [SkippableFact]
    public async Task Running_the_same_job_twice_produces_no_duplicate_audit_entry()
    {
        var (claim, txn) = await ClaimWithReserve(1_200m);

        var first = await GlJob().RunAsync(Request(claim, txn), "hf-1", false, default);
        var second = await GlJob().RunAsync(Request(claim, txn), "hf-2", false, default);

        first.Should().Be(GlPostingOutcome.Posted);
        second.Should().Be(GlPostingOutcome.AlreadyPosted);
        (await Audit(claim, AuditEventTypes.GlPostingSimulated)).Should().ContainSingle();
        (await Txn(claim, txn.Id)).PostingJobId.Should().Be("hf-1", "the original posting is not overwritten");
    }

    [SkippableFact]
    public async Task Concurrent_executions_of_the_same_job_still_post_exactly_once()
    {
        var (claim, txn) = await ClaimWithReserve(2_500m);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async i =>
        {
            try { return (Outcome: (GlPostingOutcome?)await GlJob().RunAsync(Request(claim, txn), $"hf-{i}", false, default), Error: (Exception?)null); }
            catch (Exception ex) { return (null, ex); } // losers of the race fail on the concurrency token and would be retried by Hangfire
        }));

        results.Count(r => r.Outcome == GlPostingOutcome.Posted).Should().Be(1);
        (await Audit(claim, AuditEventTypes.GlPostingSimulated)).Should().ContainSingle();
        (await Txn(claim, txn.Id)).PostingStatus.Should().Be(ReservePostingStatus.Posted);

        // A Hangfire retry of a loser now finds the work done.
        (await GlJob().RunAsync(Request(claim, txn), "hf-retry", false, default)).Should().Be(GlPostingOutcome.AlreadyPosted);
        (await Audit(claim, AuditEventTypes.GlPostingSimulated)).Should().ContainSingle();
    }

    [SkippableFact]
    public async Task A_transaction_still_pending_approval_is_never_posted()
    {
        var (claim, txn) = await ClaimWithReserve(50_000m);
        txn.ApprovalStatus.Should().Be(ReserveApprovalStatus.PendingApproval);

        var outcome = await GlJob().RunAsync(Request(claim, txn), "hf-1", false, default);

        outcome.Should().Be(GlPostingOutcome.NotApproved);
        (await Audit(claim, AuditEventTypes.GlPostingSimulated)).Should().BeEmpty();
        (await Txn(claim, txn.Id)).PostingStatus.Should().Be(ReservePostingStatus.Pending);
    }

    [SkippableFact]
    public async Task A_mismatched_key_or_unknown_transaction_is_ignored_safely()
    {
        var (claim, txn) = await ClaimWithReserve(900m);

        (await GlJob().RunAsync(Request(claim, txn) with { IdempotencyKey = "Reserve:wrong:Change:9" }, null, false, default))
            .Should().Be(GlPostingOutcome.NotFound);
        (await GlJob().RunAsync(Request(claim, txn) with { ReserveHistoryId = Guid.NewGuid() }, null, false, default))
            .Should().Be(GlPostingOutcome.NotFound);
        (await GlJob().RunAsync(Request(Guid.NewGuid(), txn), null, false, default)).Should().Be(GlPostingOutcome.NotFound);
        (await Audit(claim, AuditEventTypes.GlPostingSimulated)).Should().BeEmpty();
    }

    [SkippableFact]
    public async Task A_decrease_is_posted_as_the_mirror_entry()
    {
        var (claim, _) = await ClaimWithReserve(9_000m);
        var reverse = (await _app.Send(new SubmitReserveCommand(claim, ReserveComponentType.Indemnity, 4_000m, "Reduce", ReserveTransactionType.Reverse), Handler)).Transaction;

        await GlJob().RunAsync(Request(claim, reverse), "hf-9", false, default);

        (await Audit(claim, AuditEventTypes.GlPostingSimulated)).Should().ContainSingle()
            .Which.Description.Should().Be("Simulated GL posting: DR Outstanding Loss Reserves / CR Change in Outstanding Reserves, Amount = 4,000.00");
    }

    [SkippableFact]
    public async Task Exhausted_retries_mark_the_posting_failed_audit_it_and_allow_a_manual_retry()
    {
        var (claim, txn) = await ClaimWithReserve(3_000m);
        var flaky = new FlakyScopeFactory(_app.Root.GetRequiredService<IServiceScopeFactory>(), failFirst: 1);
        var job = new PostGlReserveChangeJob(flaky, NullLogger<PostGlReserveChangeJob>.Instance);

        // Not the final attempt: the error propagates for Hangfire to retry, and nothing is recorded yet.
        await FluentActions.Invoking(() => job.RunAsync(Request(claim, txn), "hf-1", isFinalAttempt: false, default))
            .Should().ThrowAsync<InvalidOperationException>();
        (await Txn(claim, txn.Id)).PostingStatus.Should().Be(ReservePostingStatus.Pending);
        (await Audit(claim, AuditEventTypes.GlPostingFailed)).Should().BeEmpty();

        // Final attempt fails too: status Failed + GL_POSTING_FAILED, and the exception still surfaces to Hangfire.
        flaky.Reset(failFirst: 1);
        await FluentActions.Invoking(() => job.RunAsync(Request(claim, txn), "hf-1", isFinalAttempt: true, default))
            .Should().ThrowAsync<InvalidOperationException>();
        (await Txn(claim, txn.Id)).PostingStatus.Should().Be(ReservePostingStatus.Failed);
        (await Audit(claim, AuditEventTypes.GlPostingFailed)).Should().ContainSingle()
            .Which.Description.Should().Contain("failed after 3 retries");

        // The UI's retry button: Failed → Pending, job queued after commit, and the next run posts it.
        var queued = _app.Scheduler.Enqueued.Count;
        var retried = await _app.Send(new RetryGlPostingCommand(claim, txn.Id), Handler);
        retried.PostingStatus.Should().Be(ReservePostingStatus.Pending);
        _app.Scheduler.Enqueued.Skip(queued).Should().ContainSingle().Which.Key.Should().Be(txn.IdempotencyKey);

        (await GlJob().RunAsync(Request(claim, txn), "hf-2", false, default)).Should().Be(GlPostingOutcome.Posted);
        (await Txn(claim, txn.Id)).PostingStatus.Should().Be(ReservePostingStatus.Posted);
    }

    [SkippableFact]
    public async Task Only_failed_postings_can_be_retried()
    {
        var (claim, txn) = await ClaimWithReserve(700m);

        await FluentActions.Invoking(() => _app.Send(new RetryGlPostingCommand(claim, txn.Id), Handler))
            .Should().ThrowAsync<ClaimsModule.Domain.Common.DomainException>();
    }

    // ------------------------------------------------------------------ SLA monitoring

    private async Task<Guid> NewClaim(ClaimStatus target = ClaimStatus.Draft)
    {
        Skip.IfNot(_sql.Available, "SQL Server not available");
        var policy = (await _app.Send(new SearchPoliciesQuery("POL-2025-002001"))).Single().Id;
        var created = await _app.Send(new CreateClaimCommand(
            policy, DateTimeOffset.UtcNow.AddDays(-1), "Scaffolding collapsed at the building site", "COL-LIAB",
            Parties: new[] { new PartyInput(PartyRole.Claimant, PartyType.Person, "Cleo", "Patra") },
            RiskObjects: new[] { new RiskObjectInput(AssetType.Equipment, "Scaffolding") }));
        if (target != ClaimStatus.Draft)
        {
            await _app.Send(new TransitionClaimStatusCommand(created.Id, ClaimStatus.Open));
            if (target != ClaimStatus.Open)
            {
                await _app.Send(new TransitionClaimStatusCommand(created.Id, target, Reason: "n/a", ClosureJustification: "n/a"));
            }
        }

        return created.Id;
    }

    [SkippableFact]
    public async Task Stale_draft_and_open_claims_are_flagged_without_changing_them()
    {
        var draft = await NewClaim();
        var open = await NewClaim(ClaimStatus.Open);
        await _sql.BackdateClaimAsync(draft, 50);
        await _sql.BackdateClaimAsync(open, 72);

        await SlaJob().RunAsync(default);

        foreach (var id in new[] { draft, open })
        {
            var breach = (await Audit(id, AuditEventTypes.SlaBreachDetected)).Should().ContainSingle().Subject;
            breach.Description.Should().Be("Claim has not been updated in 48 hours");
            breach.CreatedByName.Should().Be("System");
        }

        var detail = await _app.Send(new GetClaimDetailQuery(open));
        detail.Status.Should().Be(ClaimStatus.Open, "the job never changes claim status");
    }

    [SkippableFact]
    public async Task Fresh_claims_and_claims_in_other_statuses_are_not_flagged()
    {
        var fresh = await NewClaim();
        var investigating = await NewClaim(ClaimStatus.UnderInvestigation);
        var closed = await NewClaim(ClaimStatus.Closed);
        await _sql.BackdateClaimAsync(fresh, 47);
        await _sql.BackdateClaimAsync(investigating, 100);
        await _sql.BackdateClaimAsync(closed, 100);

        await SlaJob().RunAsync(default);

        foreach (var id in new[] { fresh, investigating, closed })
        {
            (await Audit(id, AuditEventTypes.SlaBreachDetected)).Should().BeEmpty();
        }
    }

    [SkippableFact]
    public async Task A_claim_is_flagged_at_most_once_per_24_hours()
    {
        var claim = await NewClaim();
        await _sql.BackdateClaimAsync(claim, 60);

        await SlaJob().RunAsync(default);
        await SlaJob().RunAsync(default);
        await SlaJob().RunAsync(default);
        (await Audit(claim, AuditEventTypes.SlaBreachDetected)).Should().ContainSingle("repeat runs within 24h add nothing");

        await _sql.BackdateAuditAsync(claim, AuditEventTypes.SlaBreachDetected, hours: 25);
        await SlaJob().RunAsync(default);

        (await Audit(claim, AuditEventTypes.SlaBreachDetected)).Should().HaveCount(2, "a new entry is added once 24 hours have passed");
    }

    // Wraps the real scope factory and fails the first N scope creations, simulating a transient infrastructure outage.
    private sealed class FlakyScopeFactory(IServiceScopeFactory inner, int failFirst) : IServiceScopeFactory
    {
        private int _remaining = failFirst;

        public void Reset(int failFirst) => _remaining = failFirst;

        public IServiceScope CreateScope() =>
            _remaining-- > 0 ? throw new InvalidOperationException("Simulated GL outage") : inner.CreateScope();
    }
}
