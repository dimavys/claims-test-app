using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Enums;

namespace ClaimsModule.Domain.Entities;

/// <summary>Simulated policy (seeded reference data standing in for a real Policy domain).</summary>
public class Policy : BaseEntity
{
    private Policy() { }

    public string PolicyNumber { get; private set; } = string.Empty;
    public string ClientName { get; private set; } = string.Empty;
    public DateOnly EffectiveDate { get; private set; }
    public DateOnly ExpirationDate { get; private set; }
    public PolicyStatus Status { get; private set; }

    /// <summary>Comma-separated coverage type names (FRS §9.10).</summary>
    public string CoverageTypes { get; private set; } = string.Empty;

    public static Policy Create(
        string policyNumber, string clientName, DateOnly effectiveDate, DateOnly expirationDate,
        PolicyStatus status, params string[] coverageTypes) => new()
    {
        PolicyNumber = policyNumber,
        ClientName = clientName,
        EffectiveDate = effectiveDate,
        ExpirationDate = expirationDate,
        Status = status,
        CoverageTypes = string.Join(',', coverageTypes),
    };

    public IReadOnlyList<string> CoverageTypeList() =>
        CoverageTypes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>True when the loss date (UTC calendar day) falls inside the effective period, inclusive.</summary>
    public bool Covers(DateTimeOffset lossDate)
    {
        var day = DateOnly.FromDateTime(lossDate.UtcDateTime);
        return day >= EffectiveDate && day <= ExpirationDate;
    }
}
