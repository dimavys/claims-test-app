using ClaimsModule.Domain.Common;
using MediatR;

namespace ClaimsModule.Application.DomainEvents;

/// <summary>Adapts a (MediatR-free) domain event to an INotification so handlers can subscribe through MediatR.</summary>
public sealed record DomainEventNotification<TEvent>(TEvent DomainEvent) : INotification where TEvent : IDomainEvent;

public static class DomainEventPublisher
{
    public static Task PublishAsync(IPublisher publisher, IDomainEvent domainEvent, CancellationToken ct)
    {
        var notificationType = typeof(DomainEventNotification<>).MakeGenericType(domainEvent.GetType());
        var notification = Activator.CreateInstance(notificationType, domainEvent)!;
        return publisher.Publish(notification, ct);
    }
}
