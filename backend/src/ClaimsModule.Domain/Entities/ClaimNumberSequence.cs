using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Entities;

/// <summary>Per organisation, per year counter row. Incremented atomically inside the claim-creation transaction.</summary>
public class ClaimNumberSequence : BaseEntity
{
    private ClaimNumberSequence() { }

    public int Year { get; private set; }
    public int LastValue { get; private set; }
}
