using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Enums;

namespace ClaimsModule.Domain.Entities;

public class ClaimParty : BaseEntity
{
    private ClaimParty() { }

    public Guid ClaimId { get; private set; }
    public PartyRole PartyRole { get; private set; }
    public PartyType PartyType { get; private set; }
    public string? FirstName { get; private set; }
    public string? LastName { get; private set; }
    public string? CompanyName { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Notes { get; private set; }

    /// <summary>False once the party is soft-removed from the claim.</summary>
    public bool IsActive { get; private set; } = true;

    public string DisplayName => PartyType == PartyType.Company
        ? CompanyName ?? string.Empty
        : $"{FirstName} {LastName}".Trim();

    public static ClaimParty Create(
        PartyRole role, PartyType type, string? firstName, string? lastName, string? companyName,
        string? email = null, string? phone = null, string? notes = null)
    {
        if (type == PartyType.Person && (string.IsNullOrWhiteSpace(firstName) || string.IsNullOrWhiteSpace(lastName)))
        {
            throw new DomainException("First and last name are required for a person.", field: "ClaimParties");
        }

        if (type == PartyType.Company && string.IsNullOrWhiteSpace(companyName))
        {
            throw new DomainException("Company name is required for a company.", field: "ClaimParties");
        }

        return new ClaimParty
        {
            PartyRole = role,
            PartyType = type,
            FirstName = type == PartyType.Person ? firstName!.Trim() : null,
            LastName = type == PartyType.Person ? lastName!.Trim() : null,
            CompanyName = type == PartyType.Company ? companyName!.Trim() : null,
            Email = email?.Trim(),
            Phone = phone?.Trim(),
            Notes = notes,
        };
    }

    internal void Attach(Guid claimId) => ClaimId = claimId;

    internal void Deactivate() => IsActive = false;
}
