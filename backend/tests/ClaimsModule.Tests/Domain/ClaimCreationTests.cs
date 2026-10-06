using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Entities;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Domain.Events;
using FluentAssertions;
using static ClaimsModule.Tests.Domain.TestData;

namespace ClaimsModule.Tests.Domain;

public class ClaimCreationTests
{
    [Fact]
    public void New_claim_starts_in_draft_and_raises_ClaimCreated()
    {
        var claim = Claim.Create("CLM-2026-0000001", ActivePolicy(), "Meridian", ClaimSeverity.Standard, Now, Loss(), Now, Handler);

        claim.Status.Should().Be(ClaimStatus.Draft);
        claim.DomainEvents.OfType<ClaimCreatedDomainEvent>().Should().ContainSingle()
            .Which.ClaimNumber.Should().Be("CLM-2026-0000001");
        claim.LossEvent.ClaimId.Should().Be(claim.Id);
    }

    [Fact]
    public void Without_a_claimant_a_critical_issue_is_recorded()
    {
        var claim = DraftClaim(withClaimant: false);

        claim.ValidationIssues.Should().Contain(i =>
            i.Code == ValidationIssueCodes.NoClaimant && i.Severity == ValidationSeverity.Critical && i.IsOutstanding);
    }

    [Fact]
    public void Adding_a_claimant_resolves_the_critical_issue()
    {
        var claim = DraftClaim(withClaimant: false);

        claim.AddParty(Claimant(), Now);

        claim.ValidationIssues.Single(i => i.Code == ValidationIssueCodes.NoClaimant).IsOutstanding.Should().BeFalse();
    }

    [Fact]
    public void Initial_parties_and_risk_objects_avoid_transient_structural_issues()
    {
        var claim = Claim.Create("CLM-2026-0000010", ActivePolicy(), "X", ClaimSeverity.Standard, Now, Loss(), Now,
            parties: new[] { Claimant() },
            riskObjects: new[] { ClaimRiskObject.Create(AssetType.Vehicle, "Truck") });

        claim.ValidationIssues.Should().BeEmpty();
        claim.DomainEvents.OfType<PartyAddedDomainEvent>().Should().ContainSingle();
        claim.RiskObjects.Single().IsPrimary.Should().BeTrue();
    }

    [Fact]
    public void Missing_policy_creates_a_warning_and_keeps_policy_null()
    {
        var claim = Claim.Create("CLM-2026-0000002", null, "Unknown insured", ClaimSeverity.Minor, Now, Loss(), Now);

        claim.PolicyId.Should().BeNull();
        claim.ValidationIssues.Should().Contain(i =>
            i.Code == ValidationIssueCodes.NoPolicy && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void Loss_date_outside_policy_period_creates_an_acknowledgeable_warning()
    {
        var expired = Policy.Create("POL-2023-000099", "Archived Corp", new DateOnly(2020, 1, 1), new DateOnly(2021, 12, 31),
            PolicyStatus.Expired, "Property");

        var claim = Claim.Create("CLM-2026-0000003", expired, "Archived Corp", ClaimSeverity.Standard, Now, Loss(), Now);

        claim.ValidationIssues.Should().Contain(i =>
            i.Code == ValidationIssueCodes.LossDateOutsidePolicy && i.RequiresAcknowledgement);
    }

    [Fact]
    public void Loss_date_inside_policy_period_creates_no_policy_warning()
    {
        DraftClaim().ValidationIssues.Should().NotContain(i => i.Code == ValidationIssueCodes.LossDateOutsidePolicy);
    }

    [Fact]
    public void Future_loss_date_is_rejected()
    {
        var act = () => Claim.Create("CLM-2026-0000004", ActivePolicy(), "X", ClaimSeverity.Minor, Now,
            Loss(Now.AddDays(1)), Now);

        act.Should().Throw<DomainException>().WithMessage("Loss date cannot be in the future.");
    }

    [Fact]
    public void Short_description_is_rejected()
    {
        var act = () => Claim.Create("CLM-2026-0000005", ActivePolicy(), "X", ClaimSeverity.Minor, Now,
            Loss() with { LossDescription = "too short" }, Now);

        act.Should().Throw<DomainException>().WithMessage("*at least 20 characters*");
    }

    [Fact]
    public void Last_claimant_cannot_be_removed()
    {
        var claim = DraftClaim();
        var claimant = claim.Parties.Single();

        var act = () => claim.RemoveParty(claimant.Id, Now);

        act.Should().Throw<DomainException>().WithMessage("*last Claimant*");
    }

    [Fact]
    public void A_second_claimant_allows_removing_the_first_and_soft_removes_it()
    {
        var claim = DraftClaim();
        var first = claim.Parties.Single();
        claim.AddParty(ClaimParty.Create(PartyRole.Claimant, PartyType.Company, null, null, "Acme Ltd"), Now);

        claim.RemoveParty(first.Id, Now);

        first.IsActive.Should().BeFalse();
        claim.Parties.Should().HaveCount(2);
    }

    [Fact]
    public void Person_party_requires_names_and_company_requires_a_name()
    {
        FluentActions.Invoking(() => ClaimParty.Create(PartyRole.Witness, PartyType.Person, "Only", null, null))
            .Should().Throw<DomainException>();
        FluentActions.Invoking(() => ClaimParty.Create(PartyRole.Attorney, PartyType.Company, null, null, " "))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void First_risk_object_is_primary()
    {
        var claim = DraftClaim(withRisk: false);
        claim.ValidationIssues.Should().Contain(i => i.Code == ValidationIssueCodes.NoRiskObjects);

        var first = claim.AddRiskObject(ClaimRiskObject.Create(AssetType.Property, "Warehouse"), Now);
        var second = claim.AddRiskObject(ClaimRiskObject.Create(AssetType.Equipment, "Forklift"), Now);

        first.IsPrimary.Should().BeTrue();
        second.IsPrimary.Should().BeFalse();
        claim.ValidationIssues.Single(i => i.Code == ValidationIssueCodes.NoRiskObjects).IsOutstanding.Should().BeFalse();
    }
}
