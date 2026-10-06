namespace ClaimsModule.Domain.Rules;

/// <summary>Exact user-facing messages from FRS §8, shared by the domain, validators and the validate endpoint.</summary>
public static class ValidationMessages
{
    public const string LossDateRequired = "Loss date is required.";
    public const string LossDateInFuture = "Loss date cannot be in the future.";
    public const string LossDateOutsidePolicy = "Loss date is outside the policy effective period.";
    public const string LossDescription = "Loss description is required and must be at least 20 characters.";
    public const string CauseOfLossInvalid = "Cause of loss code is not recognised or is inactive.";
    public const string NoPolicy = "No policy linked — claim requires policy association before financial actions are permitted.";
    public const string NoClaimant = "At least one Claimant party is required to open a claim.";
    public const string NoRiskObjects = "No risk objects are linked to the claim.";
    public const string ReserveAmount = "Reserve amount must be greater than zero.";
    public const string ReserveComponent = "Invalid reserve component type.";
    public const string AggregateLimit = "Total reserves will exceed $10,000,000. Manager override required.";
    public const string SelfApproval = "Self-approval is not permitted.";
    public const string ApproverAuthority = "Your role does not have authority to approve this reserve amount.";
}
