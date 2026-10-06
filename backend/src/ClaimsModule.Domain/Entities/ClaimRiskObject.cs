using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Enums;

namespace ClaimsModule.Domain.Entities;

public class ClaimRiskObject : BaseEntity
{
    private ClaimRiskObject() { }

    public Guid ClaimId { get; private set; }
    public AssetType AssetType { get; private set; }
    public string AssetDescription { get; private set; } = string.Empty;
    public string? DamageDescription { get; private set; }
    public bool IsPrimary { get; private set; }
    public string? AssetReference { get; private set; }

    public static ClaimRiskObject Create(
        AssetType assetType, string assetDescription, string? damageDescription = null, string? assetReference = null)
    {
        if (string.IsNullOrWhiteSpace(assetDescription))
        {
            throw new DomainException("Asset description is required.", field: "RiskObjects");
        }

        return new ClaimRiskObject
        {
            AssetType = assetType,
            AssetDescription = assetDescription.Trim(),
            DamageDescription = damageDescription,
            AssetReference = assetReference?.Trim(),
        };
    }

    internal void Attach(Guid claimId, bool isPrimary)
    {
        ClaimId = claimId;
        IsPrimary = isPrimary;
    }
}
