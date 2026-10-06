namespace ClaimsModule.Domain.Common;

/// <summary>Marker for events raised by aggregates. Dispatched by the Application layer after persistence.</summary>
public interface IDomainEvent
{
    Guid ClaimId { get; }
    DateTimeOffset OccurredAt { get; }
}
