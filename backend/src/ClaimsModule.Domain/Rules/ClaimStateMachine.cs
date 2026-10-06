using ClaimsModule.Domain.Enums;

namespace ClaimsModule.Domain.Rules;

public sealed record StatusTransitionRule(ClaimStatus From, ClaimStatus To, UserRole MinimumRole, string Conditions);

/// <summary>
/// Single source of truth for the claim lifecycle (FRS §4.2). The same rules seed the ClaimStatusTransitions
/// table and drive <see cref="Entities.Claim.TransitionTo"/>; any pair not listed is invalid.
/// </summary>
public static class ClaimStateMachine
{
    private static StatusTransitionRule R(ClaimStatus from, ClaimStatus to, string conditions, UserRole min = UserRole.Handler) =>
        new(from, to, min, conditions);

    public static IReadOnlyList<StatusTransitionRule> Rules { get; } = new[]
    {
        R(ClaimStatus.Draft, ClaimStatus.Open, "No critical validation issues remain (or all waived); at least one claimant party exists"),
        R(ClaimStatus.Open, ClaimStatus.UnderInvestigation, "Handler or supervisor; no additional conditions"),
        R(ClaimStatus.Open, ClaimStatus.PendingPayment, "At least one approved reserve exists"),
        R(ClaimStatus.Open, ClaimStatus.Closed, "All closure conditions satisfied"),
        R(ClaimStatus.Open, ClaimStatus.Withdrawn, "Withdrawal reason provided"),
        R(ClaimStatus.UnderInvestigation, ClaimStatus.Open, "Handler manually reverts"),
        R(ClaimStatus.UnderInvestigation, ClaimStatus.PendingPayment, "At least one approved reserve; liability determined"),
        R(ClaimStatus.UnderInvestigation, ClaimStatus.Closed, "All closure conditions satisfied"),
        R(ClaimStatus.UnderInvestigation, ClaimStatus.Withdrawn, "Withdrawal reason provided"),
        R(ClaimStatus.PendingPayment, ClaimStatus.Closed, "All closure conditions satisfied"),
        R(ClaimStatus.Closed, ClaimStatus.Reopened, "Reopen reason provided; supervisor role required", UserRole.Supervisor),
        R(ClaimStatus.Reopened, ClaimStatus.Open, "Immediately on reopen"),
    };

    public static StatusTransitionRule? Find(ClaimStatus from, ClaimStatus to) =>
        Rules.FirstOrDefault(r => r.From == from && r.To == to);

    public static bool IsAllowed(ClaimStatus from, ClaimStatus to) => Find(from, to) is not null;

    public static IReadOnlyList<ClaimStatus> ValidNextStatuses(ClaimStatus from) =>
        Rules.Where(r => r.From == from).Select(r => r.To).ToList();
}
