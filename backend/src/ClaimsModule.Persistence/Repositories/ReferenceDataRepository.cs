using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Entities;
using ClaimsModule.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Repositories;

public sealed class ReferenceDataRepository(ClaimsDbContext db) : IReferenceDataRepository
{
    public Task<Policy?> GetPolicyAsync(Guid id, CancellationToken ct = default) =>
        db.Policies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Policy>> SearchPoliciesAsync(string? query, int take, CancellationToken ct = default)
    {
        var policies = db.Policies.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            policies = policies.Where(p => p.PolicyNumber.Contains(query) || p.ClientName.Contains(query));
        }

        return await policies.OrderBy(p => p.PolicyNumber).Take(take).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CauseOfLossCode>> GetCauseOfLossCodesAsync(PerilCategory? category, bool activeOnly, CancellationToken ct = default)
    {
        var codes = db.CauseOfLossCodes.AsNoTracking().AsQueryable();
        if (activeOnly)
        {
            codes = codes.Where(c => c.IsActive);
        }

        if (category is { } peril)
        {
            codes = codes.Where(c => c.PerilCategory == peril);
        }

        return await codes.OrderBy(c => c.SortOrder).ToListAsync(ct);
    }

    public Task<CauseOfLossCode?> GetActiveCauseOfLossCodeAsync(string code, CancellationToken ct = default) =>
        db.CauseOfLossCodes.AsNoTracking().FirstOrDefaultAsync(c => c.Code == code && c.IsActive, ct);

    public async Task<IReadOnlyList<ClaimStatusTransition>> GetStatusTransitionsAsync(CancellationToken ct = default) =>
        await db.ClaimStatusTransitions.AsNoTracking().ToListAsync(ct);
}
