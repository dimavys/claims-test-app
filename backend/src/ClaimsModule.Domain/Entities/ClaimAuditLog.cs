using ClaimsModule.Domain.Common;

namespace ClaimsModule.Domain.Entities;

/// <summary>
/// Immutable, append-only event record (BR-A-01). Deliberately not a <see cref="BaseEntity"/>: there is no soft
/// delete or modified-by tracking because rows are never updated or deleted; persistence rejects any such attempt.
/// </summary>
public class ClaimAuditLog : ITenantEntity
{
    private ClaimAuditLog() { }

    public Guid Id { get; private set; } = SequentialGuid.Next();
    public Guid OrganisationId { get; private set; }
    public Guid ClaimId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string? OldValue { get; private set; }
    public string? NewValue { get; private set; }
    public Guid? RelatedEntityId { get; private set; }
    public string? RelatedEntityType { get; private set; }
    public Guid? CorrelationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedByUserId { get; private set; }

    public static ClaimAuditLog Create(
        Guid organisationId, Guid claimId, string eventType, string description, DateTimeOffset now,
        string? oldValue = null, string? newValue = null, Guid? relatedEntityId = null, string? relatedEntityType = null,
        Guid? correlationId = null, Guid? createdByUserId = null) => new()
    {
        OrganisationId = organisationId,
        ClaimId = claimId,
        EventType = eventType,
        Description = description,
        OldValue = oldValue,
        NewValue = newValue,
        RelatedEntityId = relatedEntityId,
        RelatedEntityType = relatedEntityType,
        CorrelationId = correlationId,
        CreatedAt = now,
        CreatedByUserId = createdByUserId,
    };

    /// <summary>Fills the tenant when the application layer did not supply it.</summary>
    public void StampTenant(Guid organisationId)
    {
        if (OrganisationId == Guid.Empty)
        {
            OrganisationId = organisationId;
        }
    }
}

public static class AuditEventTypes
{
    public const string ClaimCreated = "CLAIM_CREATED";
    public const string StatusChanged = "STATUS_CHANGED";
    public const string PartyAdded = "PARTY_ADDED";
    public const string PartyRemoved = "PARTY_REMOVED";
    public const string ReserveCreated = "RESERVE_CREATED";
    public const string ReserveAutoApproved = "RESERVE_AUTO_APPROVED";
    public const string ReserveApproved = "RESERVE_APPROVED";
    public const string ReserveRejected = "RESERVE_REJECTED";
    public const string ReserveRetracted = "RESERVE_RETRACTED";
    public const string GlPostingSimulated = "GL_POSTING_SIMULATED";
    public const string GlPostingFailed = "GL_POSTING_FAILED";
    public const string DocumentUploaded = "DOCUMENT_UPLOADED";
    public const string ClaimClosed = "CLAIM_CLOSED";
    public const string ClaimReopened = "CLAIM_REOPENED";
    public const string SlaBreachDetected = "SLA_BREACH_DETECTED";
    public const string ValidationIssueAdded = "VALIDATION_ISSUE_ADDED";
}
