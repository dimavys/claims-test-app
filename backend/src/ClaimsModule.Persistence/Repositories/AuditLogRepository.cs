using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Repositories;

public sealed class AuditLogRepository(ClaimsDbContext db) : IAuditLogRepository
{
    public void Add(ClaimAuditLog entry) => db.ClaimAuditLog.Add(entry);

    public async Task<(IReadOnlyList<ClaimAuditLog> Items, int TotalCount)> ListAsync(Guid claimId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.ClaimAuditLog.AsNoTracking().Where(a => a.ClaimId == claimId);
        var total = await query.CountAsync(ct);

        // Reverse-chronological; Id breaks ties between entries written in the same instant.
        var items = await query
            .OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public Task<ClaimAuditLog?> GetLatestAsync(Guid claimId, string eventType, CancellationToken ct = default) =>
        db.ClaimAuditLog.AsNoTracking()
            .Where(a => a.ClaimId == claimId && a.EventType == eventType)
            .OrderByDescending(a => a.CreatedAt)
            .FirstOrDefaultAsync(ct);
}
