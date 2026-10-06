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
public sealed class ClaimFlowTests : IDisposable
{
    private readonly SqlServerFixture _sql;
    private readonly AppHarness _app;

    public ClaimFlowTests(SqlServerFixture sql)
    {
        _sql = sql;
        _app = new AppHarness(sql);
    }

    public void Dispose() => _app.Dispose();

    private static void RequireSql(SqlServerFixture sql) => Skip.IfNot(sql.Available, "SQL Server not available");

    private async Task<Guid> PolicyId(string number = "POL-2024-001001") =>
        (await _app.Send(new SearchPoliciesQuery(number))).Single().Id;

    private static CreateClaimCommand Intake(Guid? policyId, InitialReserveInput? reserve = null, bool claimant = true, bool risk = true,
        DateTimeOffset? lossDate = null) => new(
        policyId,
        lossDate ?? DateTimeOffset.UtcNow.AddDays(-2),
        "Truck collided with a guardrail on the highway",
        "COL-VEH-COL",
        "Highway 9",
        12_000m,
        Parties: claimant ? new[] { new PartyInput(PartyRole.Claimant, PartyType.Person, "Ada", "Lovelace", Email: "ada@example.com") } : Array.Empty<PartyInput>(),
        RiskObjects: risk ? new[] { new RiskObjectInput(AssetType.Vehicle, "2022 Volvo FH16", "Front damage") } : Array.Empty<RiskObjectInput>(),
        InitialReserve: reserve);

    private async Task<ClaimCreatedDto> CreateOpenClaim(InitialReserveInput? reserve = null)
    {
        var created = await _app.Send(Intake(await PolicyId(), reserve));
        await _app.Send(new TransitionClaimStatusCommand(created.Id, ClaimStatus.Open));
        return created;
    }

    private Task<PagedResult<AuditEntryDto>> Audit(Guid claimId, int pageSize = 100) =>
        _app.Send(new GetClaimAuditQuery(claimId, 1, pageSize));

    // ------------------------------------------------------------------ FNOL

    [SkippableFact]
    public async Task Creating_a_claim_generates_a_number_persists_the_graph_and_audits_it()
    {
        RequireSql(_sql);

        var created = await _app.Send(Intake(await PolicyId()), Handler);

        created.ClaimNumber.Should().MatchRegex(@"^CLM-\d{4}-\d{7}$");
        created.Status.Should().Be(ClaimStatus.Draft);

        var detail = await _app.Send(new GetClaimDetailQuery(created.Id));
        detail.PolicyNumber.Should().Be("POL-2024-001001");
        detail.ClientName.Should().Be("Meridian Transport LLC");
        detail.AssignedHandlerName.Should().Be(Handler.Name);
        detail.ClaimType.Should().Be("Auto");
        detail.LossEvent.CauseOfLossName.Should().Be("Vehicle Collision");
        detail.Parties.Should().ContainSingle(p => p.PartyRole == PartyRole.Claimant && p.DisplayName == "Ada Lovelace");
        detail.RiskObjects.Should().ContainSingle(r => r.IsPrimary);
        detail.ValidNextStatuses.Select(n => n.Status).Should().Equal(ClaimStatus.Open);

        var audit = (await Audit(created.Id)).Items;
        audit.Should().Contain(a => a.EventType == AuditEventTypes.ClaimCreated && a.CreatedByName == Handler.Name);
        audit.Should().Contain(a => a.EventType == AuditEventTypes.PartyAdded);
        audit.Select(a => a.CorrelationId).Distinct().Should().ContainSingle().Which.Should().NotBeNull(
            "one request means one correlation id across all its audit entries");
    }

    [SkippableFact]
    public async Task Claim_numbers_increase_across_requests()
    {
        RequireSql(_sql);
        var policy = await PolicyId();

        var first = await _app.Send(Intake(policy));
        var second = await _app.Send(Intake(policy));

        int Seq(string n) => int.Parse(n[^7..]);
        Seq(second.ClaimNumber).Should().Be(Seq(first.ClaimNumber) + 1);
    }

    [SkippableFact]
    public async Task Intake_without_a_policy_creates_a_draft_with_a_warning_and_uses_the_claimant_as_client()
    {
        RequireSql(_sql);

        var created = await _app.Send(Intake(null));

        created.ValidationIssues.Should().Contain(i => i.Code == ValidationIssueCodes.NoPolicy && i.Severity == ValidationSeverity.Warning);
        var detail = await _app.Send(new GetClaimDetailQuery(created.Id));
        detail.PolicyId.Should().BeNull();
        detail.ClientName.Should().Be("Ada Lovelace");
    }

    [SkippableFact]
    public async Task Intake_with_an_expired_policy_records_an_acknowledgeable_warning_and_audits_it()
    {
        RequireSql(_sql);

        var created = await _app.Send(Intake(await PolicyId("POL-2023-000099")));

        created.ValidationIssues.Should().Contain(i => i.Code == ValidationIssueCodes.LossDateOutsidePolicy && i.RequiresAcknowledgement);
        (await Audit(created.Id)).Items.Should().Contain(a =>
            a.EventType == AuditEventTypes.ValidationIssueAdded && a.Description.Contains("outside the policy effective period"));
    }

    [SkippableFact]
    public async Task Intake_without_a_claimant_is_created_in_draft_with_a_critical_issue()
    {
        RequireSql(_sql);

        var created = await _app.Send(Intake(await PolicyId(), claimant: false));

        created.ValidationIssues.Should().Contain(i => i.Code == ValidationIssueCodes.NoClaimant && i.Severity == ValidationSeverity.Critical);
        await FluentActions.Invoking(() => _app.Send(new TransitionClaimStatusCommand(created.Id, ClaimStatus.Open)))
            .Should().ThrowAsync<ClaimTransitionException>();
    }

    [SkippableFact]
    public async Task Invalid_intake_is_rejected_with_all_errors_and_nothing_is_saved()
    {
        RequireSql(_sql);
        var before = (await _app.Send(new ListClaimsQuery(PageSize: 1))).TotalCount;

        var bad = Intake(await PolicyId(), lossDate: DateTimeOffset.UtcNow.AddDays(3)) with
        {
            LossDescription = "short",
            CauseOfLossCode = "COL-NOPE",
        };

        var ex = (await FluentActions.Invoking(() => _app.Send(bad)).Should().ThrowAsync<RequestValidationException>()).Which;

        ex.Errors.Keys.Should().Contain(new[] { "LossDate", "LossDescription", "CauseOfLossCode" });
        ex.Errors["LossDate"].Should().Contain("Loss date cannot be in the future.");
        ex.Errors["LossDescription"].Should().Contain("Loss description is required and must be at least 20 characters.");
        ex.Errors["CauseOfLossCode"].Should().Contain("Cause of loss code is not recognised or is inactive.");
        (await _app.Send(new ListClaimsQuery(PageSize: 1))).TotalCount.Should().Be(before);
    }

    [SkippableFact]
    public async Task Initial_reserve_without_a_policy_is_rejected()
    {
        RequireSql(_sql);

        var act = () => _app.Send(Intake(null, new InitialReserveInput(ReserveComponentType.Indemnity, 1_000m)));

        (await act.Should().ThrowAsync<RequestValidationException>()).Which.Errors.Should().ContainKey("PolicyId");
    }

    [SkippableFact]
    public async Task Initial_reserve_within_authority_is_auto_approved_audited_and_queues_one_gl_job_after_commit()
    {
        RequireSql(_sql);
        var queuedBefore = _app.Scheduler.Enqueued.Count;

        var created = await _app.Send(Intake(await PolicyId(), new InitialReserveInput(ReserveComponentType.Indemnity, 5_000m)));

        created.InitialReserve!.Transaction.ApprovalStatus.Should().Be(ReserveApprovalStatus.AutoApproved);
        created.InitialReserve.RequiresApproval.Should().BeFalse();
        _app.Scheduler.Enqueued.Skip(queuedBefore).Should().ContainSingle()
            .Which.Should().Match<(Guid ClaimId, Guid ReserveHistoryId, string Key)>(j =>
                j.ClaimId == created.Id && j.Key.StartsWith("Reserve:") && j.Key.EndsWith(":Change:1"));

        var audit = (await Audit(created.Id)).Items.Select(a => a.EventType).ToList();
        audit.Should().Contain(new[] { AuditEventTypes.ReserveCreated, AuditEventTypes.ReserveAutoApproved });
    }

    [SkippableFact]
    public async Task Initial_reserve_above_authority_waits_for_approval_and_queues_no_job()
    {
        RequireSql(_sql);
        var queuedBefore = _app.Scheduler.Enqueued.Count;

        var created = await _app.Send(Intake(await PolicyId(), new InitialReserveInput(ReserveComponentType.Indemnity, 50_000m)));

        created.InitialReserve!.Transaction.ApprovalStatus.Should().Be(ReserveApprovalStatus.PendingApproval);
        created.InitialReserve.RequiredAuthority.Should().Contain("Supervisor");
        _app.Scheduler.Enqueued.Count.Should().Be(queuedBefore);
    }

    // ------------------------------------------------------------------ lifecycle

    [SkippableFact]
    public async Task Status_transition_is_applied_audited_and_returns_the_next_options()
    {
        RequireSql(_sql);
        var created = await _app.Send(Intake(await PolicyId()));

        var result = await _app.Send(new TransitionClaimStatusCommand(created.Id, ClaimStatus.Open), Handler);

        result.Status.Should().Be(ClaimStatus.Open);
        result.ValidNextStatuses.Select(n => n.Status).Should().BeEquivalentTo(new[]
            { ClaimStatus.UnderInvestigation, ClaimStatus.PendingPayment, ClaimStatus.Closed, ClaimStatus.Withdrawn });
        (await Audit(created.Id)).Items.Should().Contain(a =>
            a.EventType == AuditEventTypes.StatusChanged && a.OldValue == "Draft" && a.NewValue == "Open");
    }

    [SkippableFact]
    public async Task Invalid_transition_returns_a_domain_error_listing_valid_statuses()
    {
        RequireSql(_sql);
        var created = await _app.Send(Intake(await PolicyId()));

        var ex = (await FluentActions.Invoking(() => _app.Send(new TransitionClaimStatusCommand(created.Id, ClaimStatus.Closed)))
            .Should().ThrowAsync<ClaimTransitionException>()).Which;

        ex.Message.Should().Be("Transition from Draft to Closed is not permitted.");
        ex.ValidNextStatuses.Should().Equal(ClaimStatus.Open);
    }

    [SkippableFact]
    public async Task Expired_policy_warning_must_be_acknowledged_to_open()
    {
        RequireSql(_sql);
        var created = await _app.Send(Intake(await PolicyId("POL-2023-000099")));

        await FluentActions.Invoking(() => _app.Send(new TransitionClaimStatusCommand(created.Id, ClaimStatus.Open)))
            .Should().ThrowAsync<ClaimTransitionException>();

        var result = await _app.Send(new TransitionClaimStatusCommand(created.Id, ClaimStatus.Open, AcknowledgeWarnings: true));
        result.Status.Should().Be(ClaimStatus.Open);
    }

    [SkippableFact]
    public async Task Closing_and_reopening_write_their_dedicated_audit_events()
    {
        RequireSql(_sql);
        var created = await CreateOpenClaim();

        await _app.Send(new TransitionClaimStatusCommand(created.Id, ClaimStatus.Closed, Reason: "Settled"), Handler);
        await FluentActions.Invoking(() => _app.Send(new TransitionClaimStatusCommand(created.Id, ClaimStatus.Reopened, Reason: "New evidence"), Handler))
            .Should().ThrowAsync<DomainException>().Where(e => e.Kind == DomainErrorKind.Authority);
        var reopened = await _app.Send(new TransitionClaimStatusCommand(created.Id, ClaimStatus.Reopened, Reason: "New evidence"), Supervisor);

        reopened.Status.Should().Be(ClaimStatus.Open);
        var types = (await Audit(created.Id)).Items.Select(a => a.EventType).ToList();
        types.Should().Contain(new[] { AuditEventTypes.ClaimClosed, AuditEventTypes.ClaimReopened });
    }

    [SkippableFact]
    public async Task Closure_preflight_reports_blockers_without_changing_the_claim()
    {
        RequireSql(_sql);
        var created = await CreateOpenClaim(new InitialReserveInput(ReserveComponentType.Indemnity, 2_000m));

        var blocked = await _app.Send(new GetClosurePreflightQuery(created.Id));
        var cleared = await _app.Send(new GetClosurePreflightQuery(created.Id, "Settled out of court"));

        blocked.CanClose.Should().BeFalse();
        blocked.RequiresJustification.Should().BeTrue();
        blocked.OpenReserveBalance.Should().Be(2_000m);
        cleared.CanClose.Should().BeTrue();
        (await _app.Send(new GetClaimDetailQuery(created.Id))).Status.Should().Be(ClaimStatus.Open);
    }

    [SkippableFact]
    public async Task Parties_can_be_added_and_the_last_claimant_cannot_be_removed()
    {
        RequireSql(_sql);
        var created = await CreateOpenClaim();
        var claimant = (await _app.Send(new GetClaimDetailQuery(created.Id))).Parties.Single();

        await FluentActions.Invoking(() => _app.Send(new RemoveClaimPartyCommand(created.Id, claimant.Id)))
            .Should().ThrowAsync<DomainException>().WithMessage("*last Claimant*");

        var witness = await _app.Send(new AddClaimPartyCommand(created.Id, new PartyInput(PartyRole.Witness, PartyType.Person, "Wes", "Ness")));
        await _app.Send(new AddClaimPartyCommand(created.Id, new PartyInput(PartyRole.Claimant, PartyType.Company, CompanyName: "Acme Ltd")));
        await _app.Send(new RemoveClaimPartyCommand(created.Id, claimant.Id));

        var detail = await _app.Send(new GetClaimDetailQuery(created.Id));
        detail.Parties.Single(p => p.Id == claimant.Id).IsActive.Should().BeFalse();
        witness.PartyRole.Should().Be(PartyRole.Witness);
        var types = (await Audit(created.Id)).Items.Select(a => a.EventType).ToList();
        types.Should().Contain(new[] { AuditEventTypes.PartyAdded, AuditEventTypes.PartyRemoved });
    }

    [SkippableFact]
    public async Task Unknown_claim_is_not_found()
    {
        RequireSql(_sql);

        await FluentActions.Invoking(() => _app.Send(new GetClaimDetailQuery(Guid.NewGuid()))).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Invoking(() => _app.Send(new TransitionClaimStatusCommand(Guid.NewGuid(), ClaimStatus.Open))).Should().ThrowAsync<NotFoundException>();
    }

    // ------------------------------------------------------------------ queries

    [SkippableFact]
    public async Task List_filters_by_status_search_and_paginates_newest_first()
    {
        RequireSql(_sql);
        var open = await CreateOpenClaim();
        var draft = await _app.Send(Intake(await PolicyId()));

        var openOnly = await _app.Send(new ListClaimsQuery(Statuses: new[] { ClaimStatus.Open }, Search: open.ClaimNumber));
        openOnly.Items.Should().ContainSingle().Which.Id.Should().Be(open.Id);

        var byNumber = await _app.Send(new ListClaimsQuery(Search: draft.ClaimNumber[^7..]));
        byNumber.Items.Should().ContainSingle().Which.Status.Should().Be(ClaimStatus.Draft);

        var byHandler = await _app.Send(new ListClaimsQuery(AssignedHandler: "Hannah", PageSize: 2));
        byHandler.Items.Should().HaveCount(2).And.OnlyContain(c => c.AssignedHandlerName == Handler.Name);
        byHandler.TotalCount.Should().BeGreaterThan(2);
        byHandler.Items.Select(c => c.ReportedDate).Should().BeInDescendingOrder();

        (await _app.Send(new ListClaimsQuery(AssignedHandler: "nobody-like-this"))).Items.Should().BeEmpty();
        var summary = byNumber.Items.Single();
        summary.CauseOfLossName.Should().Be("Vehicle Collision");
        summary.PolicyNumber.Should().Be("POL-2024-001001");
    }

    [SkippableFact]
    public async Task List_rejects_bad_paging()
    {
        RequireSql(_sql);

        await FluentActions.Invoking(() => _app.Send(new ListClaimsQuery(PageSize: 500))).Should().ThrowAsync<RequestValidationException>();
        await FluentActions.Invoking(() => _app.Send(new ListClaimsQuery(Page: 0))).Should().ThrowAsync<RequestValidationException>();
    }

    [SkippableFact]
    public async Task Audit_is_paginated_in_reverse_chronological_order()
    {
        RequireSql(_sql);
        var created = await CreateOpenClaim();
        await _app.Send(new TransitionClaimStatusCommand(created.Id, ClaimStatus.UnderInvestigation));

        var all = (await Audit(created.Id)).Items;
        all.Select(a => a.CreatedAt).Should().BeInDescendingOrder();
        all.First().EventType.Should().Be(AuditEventTypes.StatusChanged);
        all.First().NewValue.Should().Be("UnderInvestigation");

        var page = await _app.Send(new GetClaimAuditQuery(created.Id, 1, 2));
        page.Items.Should().HaveCount(2);
        page.TotalCount.Should().Be(all.Count);
        page.TotalPages.Should().Be((int)Math.Ceiling(all.Count / 2.0));
    }

    [SkippableFact]
    public async Task Validate_reports_critical_issues_and_warnings_without_saving()
    {
        RequireSql(_sql);
        var before = (await _app.Send(new ListClaimsQuery(PageSize: 1))).TotalCount;

        var report = await _app.Send(new ValidateClaimQuery(
            Intake(await PolicyId("POL-2023-000099"), claimant: false, risk: false) with { LossDescription = "tiny" }));

        report.IsValid.Should().BeFalse();
        report.Critical.Should().Contain("Loss description is required and must be at least 20 characters.")
            .And.Contain("At least one Claimant party is required to open a claim.");
        report.Warnings.Should().Contain("Loss date is outside the policy effective period.")
            .And.Contain("No risk objects are linked to the claim.");
        (await _app.Send(new ListClaimsQuery(PageSize: 1))).TotalCount.Should().Be(before);
    }

    [SkippableFact]
    public async Task Reference_data_queries_return_the_seeded_data()
    {
        RequireSql(_sql);

        var meridian = (await _app.Send(new SearchPoliciesQuery("meridian"))).Should().ContainSingle().Subject;
        meridian.CoverageTypes.Should().Equal("Vehicle", "Cargo");
        (await _app.Send(new SearchPoliciesQuery("POL-2025"))).Should().HaveCount(2);
        (await _app.Send(new SearchPoliciesQuery(null))).Should().HaveCount(5);
        (await _app.Send(new GetPolicyCoverageQuery(meridian.Id))).CoverageTypes.Should().Equal("Vehicle", "Cargo");

        (await _app.Send(new ListCauseOfLossCodesQuery())).Should().HaveCount(10);
        (await _app.Send(new ListCauseOfLossCodesQuery(PerilCategory.Auto))).Select(c => c.Code)
            .Should().BeEquivalentTo("COL-VEH-COL", "COL-VEH-COMP");

        var statuses = await _app.Send(new ListClaimStatusesQuery());
        statuses.Should().HaveCount(7);
        statuses.Single(s => s.Status == ClaimStatus.Draft).ValidNextStatuses.Single().Status.Should().Be(ClaimStatus.Open);
        statuses.Single(s => s.Status == ClaimStatus.Withdrawn).ValidNextStatuses.Should().BeEmpty();
        statuses.Single(s => s.Status == ClaimStatus.Closed).ValidNextStatuses.Single().MinimumRole.Should().Be(UserRole.Supervisor);
    }
}
