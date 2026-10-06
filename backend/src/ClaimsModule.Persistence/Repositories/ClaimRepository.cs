using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Entities;
using ClaimsModule.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Repositories;

public sealed class ClaimRepository(ClaimsDbContext db) : IClaimRepository
{
    public async Task<Claim?> GetByIdAsync(Guid id, ClaimIncludes includes, bool track, CancellationToken ct = default)
    {
        IQueryable<Claim> query = db.Claims;

        if (includes.HasFlag(ClaimIncludes.LossEvent)) { query = query.Include(c => c.LossEvent); }
        if (includes.HasFlag(ClaimIncludes.Parties)) { query = query.Include(c => c.Parties); }
        if (includes.HasFlag(ClaimIncludes.RiskObjects)) { query = query.Include(c => c.RiskObjects); }
        if (includes.HasFlag(ClaimIncludes.Issues)) { query = query.Include(c => c.ValidationIssues); }
        if (includes.HasFlag(ClaimIncludes.Documents)) { query = query.Include(c => c.Documents); }
        if (includes.HasFlag(ClaimIncludes.Reserves)) { query = query.Include(c => c.ReserveComponents).ThenInclude(r => r.History); }

        // Several collection includes would otherwise produce one huge cartesian-product query.
        query = query.AsSplitQuery();

        if (!track)
        {
            query = query.AsNoTracking();
        }

        return await query.FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task AddAsync(Claim claim, CancellationToken ct = default) => await db.Claims.AddAsync(claim, ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct = default) => db.Claims.AnyAsync(c => c.Id == id, ct);

    public async Task<(IReadOnlyList<Claim> Items, int TotalCount)> ListAsync(ClaimListFilter filter, CancellationToken ct = default)
    {
        var query = db.Claims.AsNoTracking().AsQueryable();

        if (filter.Statuses is { Count: > 0 })
        {
            var statuses = filter.Statuses.ToList();
            query = query.Where(c => statuses.Contains(c.Status));
        }

        if (filter.AssignedHandlerIds is { Count: > 0 })
        {
            var handlers = filter.AssignedHandlerIds.Cast<Guid?>().ToList();
            query = query.Where(c => handlers.Contains(c.AssignedHandlerId));
        }

        if (filter.PolicyId is { } policyId)
        {
            query = query.Where(c => c.PolicyId == policyId);
        }

        if (filter.LossDateFrom is { } from)
        {
            query = query.Where(c => c.LossEvent.LossDate >= from);
        }

        if (filter.LossDateTo is { } to)
        {
            query = query.Where(c => c.LossEvent.LossDate <= to);
        }

        if (!string.IsNullOrWhiteSpace(filter.CauseOfLossCode))
        {
            var code = filter.CauseOfLossCode;
            query = query.Where(c => c.LossEvent.CauseOfLossCode == code);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search;
            query = query.Where(c => c.ClaimNumber.Contains(term) || c.ClientName.Contains(term));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(c => c.ReportedDate).ThenByDescending(c => c.ClaimNumber)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Include(c => c.LossEvent)
            .Include(c => c.ReserveComponents)
            .AsSplitQuery()
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<IReadOnlyList<ClaimDocument>> GetDocumentsAsync(Guid claimId, CancellationToken ct = default) =>
        await db.ClaimDocuments.AsNoTracking().Where(d => d.ClaimId == claimId).ToListAsync(ct);

    public async Task<IReadOnlyList<Claim>> GetStaleClaimsAsync(DateTimeOffset notUpdatedSince, CancellationToken ct = default) =>
        await db.Claims.AsNoTracking()
            .Where(c => (c.Status == ClaimStatus.Draft || c.Status == ClaimStatus.Open)
                        && (c.UpdatedAt ?? c.CreatedAt) < notUpdatedSince)
            .ToListAsync(ct);
}
