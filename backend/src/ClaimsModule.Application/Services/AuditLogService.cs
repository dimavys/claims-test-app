using System.Text.Json;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Entities;

namespace ClaimsModule.Application.Services;

public sealed class AuditLogService(
    IAuditLogRepository repository,
    ICurrentUserService currentUser,
    ICorrelationContext correlation,
    TimeProvider time) : IAuditLogService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public void Record(
        Guid claimId, string eventType, string description, object? oldValue = null, object? newValue = null,
        Guid? relatedEntityId = null, string? relatedEntityType = null, Guid? actorId = null)
    {
        repository.Add(ClaimAuditLog.Create(
            currentUser.OrganisationId,
            claimId,
            eventType,
            description,
            time.GetUtcNow(),
            Serialize(oldValue),
            Serialize(newValue),
            relatedEntityId,
            relatedEntityType,
            correlation.CorrelationId,
            actorId ?? currentUser.UserId));
    }

    private static string? Serialize(object? value) => value switch
    {
        null => null,
        string s => s,
        _ => JsonSerializer.Serialize(value, Json),
    };
}
