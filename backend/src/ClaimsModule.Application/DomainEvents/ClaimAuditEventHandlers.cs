using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Entities;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Domain.Events;
using MediatR;

namespace ClaimsModule.Application.DomainEvents;

/// <summary>
/// Turns domain events into append-only audit entries (BR-A-02). Runs inside the command's transaction, so an
/// audit row is committed if and only if the business change is.
/// </summary>
public sealed class ClaimAuditEventHandlers(IAuditLogService audit) :
    INotificationHandler<DomainEventNotification<ClaimCreatedDomainEvent>>,
    INotificationHandler<DomainEventNotification<ClaimStatusChangedDomainEvent>>,
    INotificationHandler<DomainEventNotification<PartyAddedDomainEvent>>,
    INotificationHandler<DomainEventNotification<PartyRemovedDomainEvent>>,
    INotificationHandler<DomainEventNotification<ValidationIssueAddedDomainEvent>>,
    INotificationHandler<DomainEventNotification<ReserveSubmittedDomainEvent>>,
    INotificationHandler<DomainEventNotification<ReserveApprovedDomainEvent>>,
    INotificationHandler<DomainEventNotification<ReserveRejectedDomainEvent>>,
    INotificationHandler<DomainEventNotification<ReserveRetractedDomainEvent>>,
    INotificationHandler<DomainEventNotification<DocumentUploadedDomainEvent>>
{
    public Task Handle(DomainEventNotification<ClaimCreatedDomainEvent> n, CancellationToken ct)
    {
        var e = n.DomainEvent;
        audit.Record(e.ClaimId, AuditEventTypes.ClaimCreated, $"Claim {e.ClaimNumber} created.",
            newValue: new { e.ClaimNumber, e.PolicyId, e.CauseOfLossCode }, relatedEntityId: e.ClaimId, relatedEntityType: nameof(Claim), actorId: e.ActorId);
        return Task.CompletedTask;
    }

    public Task Handle(DomainEventNotification<ClaimStatusChangedDomainEvent> n, CancellationToken ct)
    {
        var e = n.DomainEvent;
        var reason = string.IsNullOrWhiteSpace(e.Reason) ? string.Empty : $" Reason: {e.Reason}";
        audit.Record(e.ClaimId, AuditEventTypes.StatusChanged, $"Status changed from {e.From} to {e.To}.{reason}",
            e.From.ToString(), e.To.ToString(), e.ClaimId, nameof(Claim), e.ActorId);

        if (e.To == ClaimStatus.Closed)
        {
            audit.Record(e.ClaimId, AuditEventTypes.ClaimClosed, $"Claim closed.{reason}",
                newValue: new { Reason = e.Reason }, relatedEntityId: e.ClaimId, relatedEntityType: nameof(Claim), actorId: e.ActorId);
        }
        else if (e.To == ClaimStatus.Reopened)
        {
            audit.Record(e.ClaimId, AuditEventTypes.ClaimReopened, $"Claim reopened.{reason}",
                newValue: new { Reason = e.Reason }, relatedEntityId: e.ClaimId, relatedEntityType: nameof(Claim), actorId: e.ActorId);
        }

        return Task.CompletedTask;
    }

    public Task Handle(DomainEventNotification<PartyAddedDomainEvent> n, CancellationToken ct)
    {
        var e = n.DomainEvent;
        audit.Record(e.ClaimId, AuditEventTypes.PartyAdded, $"{e.Role} '{e.DisplayName}' added.",
            newValue: new { e.Role, e.DisplayName }, relatedEntityId: e.PartyId, relatedEntityType: nameof(ClaimParty));
        return Task.CompletedTask;
    }

    public Task Handle(DomainEventNotification<PartyRemovedDomainEvent> n, CancellationToken ct)
    {
        var e = n.DomainEvent;
        audit.Record(e.ClaimId, AuditEventTypes.PartyRemoved, $"{e.Role} '{e.DisplayName}' removed.",
            oldValue: new { e.Role, e.DisplayName }, relatedEntityId: e.PartyId, relatedEntityType: nameof(ClaimParty));
        return Task.CompletedTask;
    }

    public Task Handle(DomainEventNotification<ValidationIssueAddedDomainEvent> n, CancellationToken ct)
    {
        var e = n.DomainEvent;
        audit.Record(e.ClaimId, AuditEventTypes.ValidationIssueAdded, $"{e.Severity} validation issue: {e.Message}",
            newValue: new { e.Code, e.Severity, e.Message }, relatedEntityId: e.IssueId, relatedEntityType: nameof(ClaimValidationIssue));
        return Task.CompletedTask;
    }

    public Task Handle(DomainEventNotification<ReserveSubmittedDomainEvent> n, CancellationToken ct)
    {
        var e = n.DomainEvent;
        audit.Record(e.ClaimId, AuditEventTypes.ReserveCreated,
            $"{e.Component} reserve transaction of {e.Amount:N2} submitted ({e.ApprovalStatus}).",
            newValue: new { e.Component, e.Amount, e.ApprovalStatus }, relatedEntityId: e.TransactionId, relatedEntityType: nameof(ReserveHistory), actorId: e.ActorId);
        return Task.CompletedTask;
    }

    public Task Handle(DomainEventNotification<ReserveApprovedDomainEvent> n, CancellationToken ct)
    {
        var e = n.DomainEvent;
        audit.Record(e.ClaimId,
            e.AutoApproved ? AuditEventTypes.ReserveAutoApproved : AuditEventTypes.ReserveApproved,
            e.AutoApproved
                ? $"{e.Component} reserve of {e.Amount:N2} auto-approved (within authority limit)."
                : $"{e.Component} reserve of {e.Amount:N2} approved.",
            newValue: new { e.Component, e.Amount }, relatedEntityId: e.TransactionId, relatedEntityType: nameof(ReserveHistory), actorId: e.ActorId);
        return Task.CompletedTask;
    }

    public Task Handle(DomainEventNotification<ReserveRejectedDomainEvent> n, CancellationToken ct)
    {
        var e = n.DomainEvent;
        audit.Record(e.ClaimId, AuditEventTypes.ReserveRejected, $"Reserve of {e.Amount:N2} rejected: {e.Reason}",
            oldValue: new { e.Amount, RejectionReason = e.Reason }, relatedEntityId: e.TransactionId, relatedEntityType: nameof(ReserveHistory), actorId: e.ActorId);
        return Task.CompletedTask;
    }

    public Task Handle(DomainEventNotification<ReserveRetractedDomainEvent> n, CancellationToken ct)
    {
        var e = n.DomainEvent;
        audit.Record(e.ClaimId, AuditEventTypes.ReserveRetracted, $"Pending reserve of {e.Amount:N2} retracted by submitter.",
            oldValue: new { e.Amount }, relatedEntityId: e.TransactionId, relatedEntityType: nameof(ReserveHistory), actorId: e.ActorId);
        return Task.CompletedTask;
    }

    public Task Handle(DomainEventNotification<DocumentUploadedDomainEvent> n, CancellationToken ct)
    {
        var e = n.DomainEvent;
        audit.Record(e.ClaimId, AuditEventTypes.DocumentUploaded, $"Document '{e.DocumentName}' uploaded.",
            newValue: new { e.DocumentName }, relatedEntityId: e.DocumentId, relatedEntityType: nameof(ClaimDocument), actorId: e.ActorId);
        return Task.CompletedTask;
    }
}
