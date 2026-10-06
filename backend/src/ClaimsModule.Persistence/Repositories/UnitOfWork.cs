using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence.Repositories;

public sealed class UnitOfWork(ClaimsDbContext db) : IUnitOfWork
{
    public async Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
    {
        // A user-initiated transaction must run inside the retrying execution strategy as one retriable unit.
        var strategy = db.Database.CreateExecutionStrategy();
        var attempt = 0;

        return await strategy.ExecuteAsync(async token =>
        {
            if (attempt++ > 0)
            {
                db.ChangeTracker.Clear(); // discard the half-built state of the failed attempt
            }

            await using var transaction = await db.Database.BeginTransactionAsync(token);
            var result = await operation(token);
            await transaction.CommitAsync(token);
            return result;
        }, ct);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public IReadOnlyList<IDomainEvent> TakeDomainEvents()
    {
        var aggregates = db.ChangeTracker.Entries<AggregateRoot>().Select(e => e.Entity).ToList();
        var events = aggregates.SelectMany(a => a.DomainEvents).ToList();
        aggregates.ForEach(a => a.ClearDomainEvents());
        return events;
    }
}
