using ClaimsModule.Domain.Entities;
using ClaimsModule.Domain.Enums;

namespace ClaimsModule.Tests.Domain;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    public static readonly Guid Handler = Guid.Parse("11111111-0000-0000-0000-000000000001");
    public static readonly Guid Supervisor = Guid.Parse("11111111-0000-0000-0000-000000000002");
    public static readonly Guid Manager = Guid.Parse("11111111-0000-0000-0000-000000000003");

    public static Policy ActivePolicy() => Policy.Create(
        "POL-2024-001001", "Meridian Transport LLC", new DateOnly(2024, 1, 1), new DateOnly(2026, 12, 31),
        PolicyStatus.Active, "Vehicle", "Cargo");

    public static LossDetails Loss(DateTimeOffset? lossDate = null) => new(
        lossDate ?? Now.AddDays(-3), "Truck collided with a guardrail on the highway", "COL-VEH-COL", "Highway 9", 12_000m);

    public static ClaimParty Claimant() =>
        ClaimParty.Create(PartyRole.Claimant, PartyType.Person, "Ada", "Lovelace", null, "ada@example.com");

    public static Claim DraftClaim(Policy? policy = null, bool withClaimant = true, bool withRisk = true)
    {
        var claim = Claim.Create("CLM-2026-0000001", policy ?? ActivePolicy(), "Meridian Transport LLC",
            ClaimSeverity.Standard, Now, Loss(), Now, Handler);
        if (withClaimant)
        {
            claim.AddParty(Claimant(), Now);
        }

        if (withRisk)
        {
            claim.AddRiskObject(ClaimRiskObject.Create(AssetType.Vehicle, "2022 Volvo FH16", "Front end damage"), Now);
        }

        claim.ClearDomainEvents();
        return claim;
    }

    public static Claim OpenClaim(Policy? policy = null)
    {
        var claim = DraftClaim(policy);
        claim.TransitionTo(ClaimStatus.Open, UserRole.Handler, Now, Handler, acknowledgeWarnings: true);
        claim.ClearDomainEvents();
        return claim;
    }
}
