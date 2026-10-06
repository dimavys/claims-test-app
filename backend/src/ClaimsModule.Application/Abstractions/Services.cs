namespace ClaimsModule.Application.Abstractions;

public interface IAuditLogService
{
    /// <summary>
    /// The only way to write audit entries (FRS §14.2). Adds an append-only row to the current unit of work,
    /// stamped with the request's correlation id and actor.
    /// </summary>
    void Record(
        Guid claimId,
        string eventType,
        string description,
        object? oldValue = null,
        object? newValue = null,
        Guid? relatedEntityId = null,
        string? relatedEntityType = null,
        Guid? actorId = null);
}

public interface ICorrelationContext
{
    Guid CorrelationId { get; }
}

public interface IUserDirectory
{
    string? GetDisplayName(Guid? userId);
    IReadOnlyList<Guid> FindByName(string text);
}

/// <summary>Document storage behind a provider-agnostic interface (Azure Blob or local file system).</summary>
public interface IStorageService
{
    /// <summary>Stores the content under {relativePath} and returns the full stored path (including the container).</summary>
    Task<string> UploadAsync(string relativePath, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Short-lived download URL. Document bytes never flow through the API.</summary>
    Task<Uri> GetDownloadUrlAsync(string storedPath, TimeSpan timeToLive, CancellationToken ct = default);
}

public interface IBackgroundJobScheduler
{
    /// <summary>Queues the GL posting simulation for an approved reserve transaction.</summary>
    void EnqueueGlPosting(Guid organisationId, Guid claimId, Guid reserveHistoryId, string idempotencyKey);
}

/// <summary>Side effects (e.g. queueing jobs) that must only run once the transaction has committed.</summary>
public interface IAfterCommitActions
{
    void Add(Func<CancellationToken, Task> action);
    void Clear();
    Task RunAsync(CancellationToken ct);
}
