using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Events;
using MediatR;

namespace ClaimsModule.Application.DomainEvents;

/// <summary>
/// Queues the GL posting simulation for approved reserves. Enqueueing is deferred until the transaction has
/// committed so the job can never run before the approval is visible (BR-R-02: no GL posting before approval).
/// </summary>
public sealed class GlPostingEventHandlers(
    IAfterCommitActions afterCommit,
    IBackgroundJobScheduler scheduler,
    ICurrentUserService currentUser) :
    INotificationHandler<DomainEventNotification<ReserveApprovedDomainEvent>>,
    INotificationHandler<DomainEventNotification<GlPostingRetryRequestedDomainEvent>>
{
    public Task Handle(DomainEventNotification<ReserveApprovedDomainEvent> n, CancellationToken ct)
    {
        var e = n.DomainEvent;
        Enqueue(e.ClaimId, e.TransactionId, e.IdempotencyKey);
        return Task.CompletedTask;
    }

    public Task Handle(DomainEventNotification<GlPostingRetryRequestedDomainEvent> n, CancellationToken ct)
    {
        var e = n.DomainEvent;
        Enqueue(e.ClaimId, e.TransactionId, e.IdempotencyKey);
        return Task.CompletedTask;
    }

    private void Enqueue(Guid claimId, Guid transactionId, string idempotencyKey)
    {
        var organisationId = currentUser.OrganisationId;
        afterCommit.Add(_ =>
        {
            scheduler.EnqueueGlPosting(organisationId, claimId, transactionId, idempotencyKey);
            return Task.CompletedTask;
        });
    }
}
