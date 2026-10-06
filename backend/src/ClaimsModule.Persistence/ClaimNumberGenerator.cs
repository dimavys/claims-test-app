using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence;

/// <summary>
/// Atomic, gap-free per-organisation/per-year counter. A single MERGE increments (or creates) the counter row and
/// returns the new value. Because the increment runs inside the caller's transaction, the row lock serialises
/// concurrent claim creations and a rollback gives the number back.
/// </summary>
public sealed class ClaimNumberGenerator : IClaimNumberGenerator
{
    private readonly ClaimsDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;

    public ClaimNumberGenerator(ClaimsDbContext db, ICurrentUserService currentUser, TimeProvider time)
    {
        _db = db;
        _currentUser = currentUser;
        _time = time;
    }

    public async Task<string> NextAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var year = now.Year;
        var organisationId = _currentUser.OrganisationId;
        var newId = Domain.Common.SequentialGuid.Next();

        var result = await _db.Database.SqlQuery<int>($"""
            MERGE ClaimNumberSequences WITH (HOLDLOCK) AS target
            USING (SELECT {organisationId} AS OrganisationId, {year} AS [Year]) AS source
                ON target.OrganisationId = source.OrganisationId AND target.[Year] = source.[Year]
            WHEN MATCHED THEN
                UPDATE SET LastValue = target.LastValue + 1, UpdatedAt = {now}
            WHEN NOT MATCHED THEN
                INSERT (ClaimNumberSequenceId, OrganisationId, [Year], LastValue, IsDeleted, CreatedAt)
                VALUES ({newId}, {organisationId}, {year}, 1, 0, {now})
            OUTPUT inserted.LastValue AS [Value];
            """).ToListAsync(cancellationToken);

        return ClaimNumberFormatter.Format(year, result.Single());
    }
}
