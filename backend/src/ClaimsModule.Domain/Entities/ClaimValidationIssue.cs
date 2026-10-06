using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Enums;

namespace ClaimsModule.Domain.Entities;

public static class ValidationIssueCodes
{
    public const string NoPolicy = "NO_POLICY";
    public const string LossDateOutsidePolicy = "LOSS_DATE_OUTSIDE_POLICY";
    public const string NoClaimant = "NO_CLAIMANT";
    public const string NoRiskObjects = "NO_RISK_OBJECTS";
    public const string AggregateReserveLimit = "AGGREGATE_RESERVE_LIMIT";
}

/// <summary>
/// A Critical (blocking) or Warning (non-blocking) issue recorded against a claim (FRS §5.4).
/// An issue is outstanding until it is resolved or acknowledged/waived.
/// </summary>
public class ClaimValidationIssue : BaseEntity
{
    private ClaimValidationIssue() { }

    public Guid ClaimId { get; private set; }
    public ValidationSeverity Severity { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string? Field { get; private set; }
    public string Message { get; private set; } = string.Empty;

    /// <summary>When true the issue must be cleared or acknowledged before Draft → Open (BR-C-02).</summary>
    public bool RequiresAcknowledgement { get; private set; }

    public bool IsResolved { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public bool IsAcknowledged { get; private set; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public Guid? AcknowledgedByUserId { get; private set; }

    public bool IsOutstanding => !IsResolved && !IsAcknowledged;

    internal static ClaimValidationIssue Create(
        Guid claimId, ValidationSeverity severity, string code, string? field, string message, bool requiresAcknowledgement) => new()
    {
        ClaimId = claimId,
        Severity = severity,
        Code = code,
        Field = field,
        Message = message,
        RequiresAcknowledgement = requiresAcknowledgement,
    };

    internal void Resolve(DateTimeOffset now)
    {
        IsResolved = true;
        ResolvedAt = now;
    }

    internal void Acknowledge(DateTimeOffset now, Guid? userId)
    {
        IsAcknowledged = true;
        AcknowledgedAt = now;
        AcknowledgedByUserId = userId;
    }
}
