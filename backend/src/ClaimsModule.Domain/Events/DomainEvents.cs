using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Enums;

namespace ClaimsModule.Domain.Events;

public abstract record ClaimDomainEvent(Guid ClaimId) : IDomainEvent
{
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ClaimCreatedDomainEvent(Guid ClaimId, string ClaimNumber, Guid? PolicyId, string CauseOfLossCode, Guid? ActorId) : ClaimDomainEvent(ClaimId);

public sealed record ClaimStatusChangedDomainEvent(Guid ClaimId, ClaimStatus From, ClaimStatus To, string? Reason, Guid? ActorId) : ClaimDomainEvent(ClaimId);

public sealed record PartyAddedDomainEvent(Guid ClaimId, Guid PartyId, PartyRole Role, string DisplayName) : ClaimDomainEvent(ClaimId);

public sealed record PartyRemovedDomainEvent(Guid ClaimId, Guid PartyId, PartyRole Role, string DisplayName) : ClaimDomainEvent(ClaimId);

public sealed record ValidationIssueAddedDomainEvent(Guid ClaimId, Guid IssueId, ValidationSeverity Severity, string Code, string Message) : ClaimDomainEvent(ClaimId);

public sealed record ReserveSubmittedDomainEvent(Guid ClaimId, Guid ReserveComponentId, Guid TransactionId, ReserveComponentType Component, decimal Amount, ReserveApprovalStatus ApprovalStatus, Guid? ActorId) : ClaimDomainEvent(ClaimId);

/// <summary>Raised for both automatic and manual approval; GL posting is enqueued from this event.</summary>
public sealed record ReserveApprovedDomainEvent(Guid ClaimId, Guid ReserveComponentId, Guid TransactionId, ReserveComponentType Component, decimal Amount, bool AutoApproved, string IdempotencyKey, Guid? ActorId) : ClaimDomainEvent(ClaimId);

public sealed record ReserveRejectedDomainEvent(Guid ClaimId, Guid ReserveComponentId, Guid TransactionId, decimal Amount, string Reason, Guid? ActorId) : ClaimDomainEvent(ClaimId);

public sealed record ReserveRetractedDomainEvent(Guid ClaimId, Guid ReserveComponentId, Guid TransactionId, decimal Amount, Guid? ActorId) : ClaimDomainEvent(ClaimId);

public sealed record DocumentUploadedDomainEvent(Guid ClaimId, Guid DocumentId, string DocumentName, Guid? ActorId) : ClaimDomainEvent(ClaimId);

/// <summary>A user asked to re-run a GL posting that failed after all retries. Handled by re-enqueueing the job; not audited.</summary>
public sealed record GlPostingRetryRequestedDomainEvent(Guid ClaimId, Guid ReserveComponentId, Guid TransactionId, string IdempotencyKey) : ClaimDomainEvent(ClaimId);
