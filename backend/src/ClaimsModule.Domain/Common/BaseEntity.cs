namespace ClaimsModule.Domain.Common;

/// <summary>Conventions shared by every business table: PK, tenant, soft delete and audit columns (FRS §15.1).</summary>
public abstract class BaseEntity : ITenantEntity
{
    public Guid Id { get; protected set; } = SequentialGuid.Next();
    public Guid OrganisationId { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }
    public Guid? UserCreated { get; private set; }
    public Guid? UserModified { get; private set; }

    /// <summary>Called by the persistence layer when the entity is first saved.</summary>
    public void StampCreated(Guid organisationId, DateTimeOffset now, Guid? userId)
    {
        if (OrganisationId == Guid.Empty)
        {
            OrganisationId = organisationId;
        }

        CreatedAt = now;
        UserCreated = userId;
    }

    public void StampModified(DateTimeOffset now, Guid? userId)
    {
        UpdatedAt = now;
        UserModified = userId;
    }

    public void MarkDeleted(DateTimeOffset now, Guid? userId)
    {
        IsDeleted = true;
        DeletedAt = now;
        StampModified(now, userId);
    }
}
