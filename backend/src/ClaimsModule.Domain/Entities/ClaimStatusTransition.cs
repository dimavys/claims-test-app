using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Enums;

namespace ClaimsModule.Domain.Entities;

/// <summary>Allowed workflow transition (reference data seeded from <see cref="Rules.ClaimStateMachine"/>).</summary>
public class ClaimStatusTransition : BaseEntity
{
    private ClaimStatusTransition() { }

    public ClaimStatus FromStatus { get; private set; }
    public ClaimStatus ToStatus { get; private set; }

    /// <summary>Minimum role allowed to perform the transition.</summary>
    public UserRole RequiredPermission { get; private set; }

    public string? Description { get; private set; }
}
