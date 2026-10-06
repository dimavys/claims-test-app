using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Domain.Rules;

namespace ClaimsModule.Domain.Entities;

/// <summary>One reserve line per component type per claim. CurrentAmount is the sum of approved history deltas.</summary>
public class ClaimReserveComponent : AggregateRoot
{
    private readonly List<ReserveHistory> _history = new();

    private ClaimReserveComponent() { }

    internal ClaimReserveComponent(Guid claimId, ReserveComponentType component)
    {
        ClaimId = claimId;
        Component = component;
        Status = ReserveComponentStatus.Active;
    }

    public Guid ClaimId { get; private set; }
    public ReserveComponentType Component { get; private set; }
    public decimal CurrentAmount { get; private set; }
    public ReserveComponentStatus Status { get; private set; }
    public string? Notes { get; private set; }
    public IReadOnlyCollection<ReserveHistory> History => _history;

    public bool AllowsNegativeBalance => ReserveAuthorityPolicy.AllowsNegativeBalance(Component);

    public decimal PendingAmount => _history.Where(h => h.IsPending).Sum(h => h.Amount);

    internal int NextChangeSequence => _history.Count == 0 ? 1 : _history.Max(h => h.ChangeSequence) + 1;

    internal ReserveHistory AddTransaction(ReserveTransactionType type, decimal delta, string changeReason, Guid? submittedBy)
    {
        var txn = ReserveHistory.Create(this, type, delta, changeReason, submittedBy, NextChangeSequence);
        _history.Add(txn);
        return txn;
    }

    /// <summary>Applies an approved transaction to the running balance.</summary>
    internal void ApplyApproved(ReserveHistory txn)
    {
        var previous = CurrentAmount;
        CurrentAmount += txn.Amount;
        txn.SetBalances(previous, CurrentAmount);
    }
}
