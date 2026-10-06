using System.Security.Cryptography;
using System.Text;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Domain.Rules;

namespace ClaimsModule.Persistence.Seed;

/// <summary>
/// Reference data applied through EF <c>HasData</c> (i.e. inside migrations, never at application startup).
/// Seed ids are derived deterministically from a natural key so no GUID literals are scattered through the code.
/// </summary>
public static class SeedData
{
    /// <summary>The single seeded tenant (FRS §15.1). The runtime tenant is configured via Tenant:OrganisationId.</summary>
    public static readonly Guid DefaultOrganisationId = Guid.Parse("0b1f6e3a-5a2d-4a53-9c2e-0e1d6f6a1001");

    /// <summary>HasData needs constant values, so every seeded row shares this creation timestamp.</summary>
    public static readonly DateTimeOffset SeededAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static Guid IdFor(string naturalKey)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(naturalKey));
        return new Guid(hash);
    }

    public static object[] Policies() => new object[]
    {
        Policy("POL-2024-001001", "Meridian Transport LLC", new(2024, 1, 1), new(2026, 12, 31), PolicyStatus.Active, "Vehicle,Cargo"),
        Policy("POL-2024-001002", "Harborview Properties Inc", new(2024, 6, 1), new(2026, 5, 31), PolicyStatus.Expired, "Property,Liability"),
        Policy("POL-2025-002001", "Coastal Builders Group", new(2025, 3, 1), new(2027, 2, 28), PolicyStatus.Active, "Property,Equipment"),
        Policy("POL-2025-002002", "Stanton Medical Group", new(2025, 1, 1), new(2026, 12, 31), PolicyStatus.Active, "Liability,Vehicle"),
        Policy("POL-2023-000099", "Archived Corp", new(2020, 1, 1), new(2021, 12, 31), PolicyStatus.Expired, "Property"),
    };

    public static object[] CauseOfLossCodes() => new object[]
    {
        Cause("COL-FIRE", "Fire", PerilCategory.Property, 1),
        Cause("COL-FLOOD", "Flood", PerilCategory.Weather, 2),
        Cause("COL-THEFT", "Theft", PerilCategory.Crime, 3),
        Cause("COL-VEH-COL", "Vehicle Collision", PerilCategory.Auto, 4),
        Cause("COL-VEH-COMP", "Vehicle Comprehensive", PerilCategory.Auto, 5),
        Cause("COL-LIAB", "Third Party Liability", PerilCategory.Liability, 6),
        Cause("COL-EQUIP", "Equipment Breakdown", PerilCategory.Equipment, 7),
        Cause("COL-WIND", "Wind / Storm", PerilCategory.Weather, 8),
        Cause("COL-INJURY", "Bodily Injury", PerilCategory.Liability, 9),
        Cause("COL-OTHER", "Other / Unknown", PerilCategory.General, 10),
    };

    public static object[] StatusTransitions() => ClaimStateMachine.Rules.Select(r => (object)new
    {
        Id = IdFor($"transition:{r.From}->{r.To}"),
        OrganisationId = DefaultOrganisationId,
        FromStatus = r.From,
        ToStatus = r.To,
        RequiredPermission = r.MinimumRole,
        Description = r.Conditions,
        IsDeleted = false,
        CreatedAt = SeededAt,
    }).ToArray();

    private static object Policy(string number, string client, DateOnly from, DateOnly to, PolicyStatus status, string coverage) => new
    {
        Id = IdFor($"policy:{number}"),
        OrganisationId = DefaultOrganisationId,
        PolicyNumber = number,
        ClientName = client,
        EffectiveDate = from,
        ExpirationDate = to,
        Status = status,
        CoverageTypes = coverage,
        IsDeleted = false,
        CreatedAt = SeededAt,
    };

    private static object Cause(string code, string name, PerilCategory category, int order) => new
    {
        Id = IdFor($"cause-of-loss:{code}"),
        OrganisationId = DefaultOrganisationId,
        Code = code,
        Name = name,
        PerilCategory = category,
        IsActive = true,
        SortOrder = order,
        IsDeleted = false,
        CreatedAt = SeededAt,
    };
}
