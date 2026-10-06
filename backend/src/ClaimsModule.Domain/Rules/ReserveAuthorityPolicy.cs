using ClaimsModule.Domain.Enums;

namespace ClaimsModule.Domain.Rules;

/// <summary>Three-tier reserve authority (FRS §6.3). Thresholds apply to the transaction amount, not the running total.</summary>
public static class ReserveAuthorityPolicy
{
    public const decimal AutoApprovalLimit = 10_000m;
    public const decimal SupervisorLimit = 100_000m;
    public const decimal ManagerLimit = 10_000_000m;

    /// <summary>Maximum total of reserves on one claim before a Manager override is required (BR-R-05).</summary>
    public const decimal AggregateLimit = 10_000_000m;

    public static AuthorityLevel RequiredLevel(decimal transactionAmount)
    {
        var size = Math.Abs(transactionAmount);
        if (size <= AutoApprovalLimit)
        {
            return AuthorityLevel.Auto;
        }

        return size <= SupervisorLimit ? AuthorityLevel.Supervisor : AuthorityLevel.Manager;
    }

    public static bool CanApprove(UserRole role, decimal transactionAmount) => RequiredLevel(transactionAmount) switch
    {
        AuthorityLevel.Auto => true,
        AuthorityLevel.Supervisor => role >= UserRole.Supervisor && Math.Abs(transactionAmount) <= ManagerLimit,
        _ => role >= UserRole.Manager && Math.Abs(transactionAmount) <= ManagerLimit,
    };

    public static bool AllowsNegativeBalance(ReserveComponentType component) =>
        component == ReserveComponentType.SubrogationRecoverable;

    public static string Describe(AuthorityLevel level) => level switch
    {
        AuthorityLevel.Auto => "Auto-approved (≤ $10,000)",
        AuthorityLevel.Supervisor => "Supervisor approval required",
        _ => "Manager approval required",
    };
}
