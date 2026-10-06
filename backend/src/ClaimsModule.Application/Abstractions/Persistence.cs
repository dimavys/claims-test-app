using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Entities;
using ClaimsModule.Domain.Enums;

namespace ClaimsModule.Application.Abstractions;

[Flags]
public enum ClaimIncludes
{
    None = 0,
    LossEvent = 1,
    Parties = 2,
    RiskObjects = 4,
    Reserves = 8,
    Issues = 16,
    Documents = 32,

    /// <summary>Everything the aggregate's behaviour needs for a write operation.</summary>
    ForUpdate = LossEvent | Parties | RiskObjects | Reserves | Issues,
    All = ForUpdate | Documents,
}

public sealed record ClaimListFilter(
    IReadOnlyList<ClaimStatus>? Statuses = null,
    DateTimeOffset? LossDateFrom = null,
    DateTimeOffset? LossDateTo = null,
    IReadOnlyList<Guid>? AssignedHandlerIds = null,
    string? CauseOfLossCode = null,
    Guid? PolicyId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

public interface IClaimRepository
{
    /// <summary>Loads a claim aggregate. <paramref name="track"/> must be true for operations that modify it.</summary>
    Task<Claim?> GetByIdAsync(Guid id, ClaimIncludes includes, bool track, CancellationToken ct = default);

    Task AddAsync(Claim claim, CancellationToken ct = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);

    /// <summary>Filtered, paged list (newest first) including loss event and reserves for the summary columns.</summary>
    Task<(IReadOnlyList<Claim> Items, int TotalCount)> ListAsync(ClaimListFilter filter, CancellationToken ct = default);

    Task<IReadOnlyList<ClaimDocument>> GetDocumentsAsync(Guid claimId, CancellationToken ct = default);

    /// <summary>Draft/Open claims whose UpdatedAt (or CreatedAt when never updated) is older than the cut-off.</summary>
    Task<IReadOnlyList<Claim>> GetStaleClaimsAsync(DateTimeOffset notUpdatedSince, CancellationToken ct = default);
}

public interface IReferenceDataRepository
{
    Task<Policy?> GetPolicyAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Policy>> SearchPoliciesAsync(string? query, int take, CancellationToken ct = default);
    Task<IReadOnlyList<CauseOfLossCode>> GetCauseOfLossCodesAsync(PerilCategory? category, bool activeOnly, CancellationToken ct = default);
    Task<CauseOfLossCode?> GetActiveCauseOfLossCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<ClaimStatusTransition>> GetStatusTransitionsAsync(CancellationToken ct = default);
}

public interface IAuditLogRepository
{
    void Add(ClaimAuditLog entry);
    Task<(IReadOnlyList<ClaimAuditLog> Items, int TotalCount)> ListAsync(Guid claimId, int page, int pageSize, CancellationToken ct = default);
    Task<ClaimAuditLog?> GetLatestAsync(Guid claimId, string eventType, CancellationToken ct = default);
}

public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="operation"/> in a database transaction (wrapped in the provider's retry strategy) and commits
    /// when it completes. The operation may be re-executed on a transient failure, so it must not have side effects
    /// outside the database.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Returns the domain events raised by tracked aggregates and clears them.</summary>
    IReadOnlyList<IDomainEvent> TakeDomainEvents();
}
