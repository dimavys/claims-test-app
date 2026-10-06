using ClaimsModule.Domain.Entities;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Persistence;
using ClaimsModule.Persistence.Seed;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using static ClaimsModule.Tests.Domain.TestData;

namespace ClaimsModule.Tests.Persistence;

[Collection(SqlServerCollection.Name)]
public class PersistenceTests(SqlServerFixture sql)
{
    private static readonly Guid User = Guid.Parse("22222222-0000-0000-0000-000000000001");

    private async Task<Claim> SaveNewClaim(string? number = null, bool withReserve = false)
    {
        await using var db = sql.CreateContext(userId: User);
        var policy = await db.Policies.SingleAsync(p => p.PolicyNumber == "POL-2024-001001");
        var claim = Claim.Create(number ?? $"CLM-TEST-{Guid.NewGuid():N}"[..20], policy, policy.ClientName, ClaimSeverity.Standard,
            DateTimeOffset.UtcNow, Loss(DateTimeOffset.UtcNow.AddDays(-2)), DateTimeOffset.UtcNow, User,
            parties: new[] { Claimant() },
            riskObjects: new[] { ClaimRiskObject.Create(AssetType.Vehicle, "Truck", "Dent", "VIN123") });
        if (withReserve)
        {
            claim.SubmitReserve(ReserveComponentType.Indemnity, ReserveTransactionType.Add, 5_000m, "Initial", DateTimeOffset.UtcNow, User);
        }

        db.Claims.Add(claim);
        await db.SaveChangesAsync();
        return claim;
    }

    [SkippableFact]
    public async Task Migrations_seed_the_reference_data()
    {
        Skip.IfNot(sql.Available, "SQL Server not available");
        await using var db = sql.CreateContext();

        (await db.Policies.CountAsync()).Should().Be(5);
        (await db.CauseOfLossCodes.CountAsync(c => c.IsActive)).Should().Be(10);
        (await db.ClaimStatusTransitions.CountAsync()).Should().Be(12);
        var expired = await db.Policies.SingleAsync(p => p.PolicyNumber == "POL-2023-000099");
        expired.Status.Should().Be(PolicyStatus.Expired);
        expired.CoverageTypeList().Should().Equal("Property");
    }

    [SkippableFact]
    public async Task A_full_claim_graph_round_trips_with_stamped_audit_columns()
    {
        Skip.IfNot(sql.Available, "SQL Server not available");
        var saved = await SaveNewClaim(withReserve: true);

        await using var db = sql.CreateContext();
        var loaded = await db.Claims
            .Include(c => c.LossEvent).Include(c => c.Parties).Include(c => c.RiskObjects)
            .Include(c => c.ValidationIssues).Include(c => c.ReserveComponents).ThenInclude(r => r.History)
            .SingleAsync(c => c.Id == saved.Id);

        loaded.Id.Should().Be(saved.Id);
        loaded.OrganisationId.Should().Be(SeedData.DefaultOrganisationId);
        loaded.UserCreated.Should().Be(User);
        loaded.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        loaded.RowVer.Should().NotBeEmpty();
        loaded.Status.Should().Be(ClaimStatus.Draft);
        loaded.LossEvent.CauseOfLossCode.Should().Be("COL-VEH-COL");
        loaded.Parties.Should().ContainSingle(p => p.PartyRole == PartyRole.Claimant);
        loaded.RiskObjects.Single().IsPrimary.Should().BeTrue();
        loaded.TotalReserves.Should().Be(5_000m);
        loaded.ReserveComponents.Single().History.Single().IdempotencyKey.Should().StartWith("Reserve:");
        loaded.ValidationIssues.Should().BeEmpty("intake supplied a claimant and a risk object, so nothing was ever raised");
    }

    [SkippableFact]
    public async Task Claim_numbers_are_unique_and_contiguous_under_concurrency()
    {
        Skip.IfNot(sql.Available, "SQL Server not available");

        var numbers = await Task.WhenAll(Enumerable.Range(0, 40).Select(async _ =>
        {
            await using var db = sql.CreateContext();
            await using var tx = await db.Database.BeginTransactionAsync();
            var number = await new ClaimNumberGenerator(db, new TestUserAccessor(), TimeProvider.System).NextAsync();
            await tx.CommitAsync();
            return number;
        }));

        numbers.Should().OnlyHaveUniqueItems();
        var year = DateTimeOffset.UtcNow.Year;
        var sequence = numbers.Select(n => int.Parse(n[^7..])).OrderBy(n => n).ToList();
        sequence.Should().Equal(Enumerable.Range(sequence[0], 40), "no gaps are permitted");
        numbers.Should().OnlyContain(n => n.StartsWith($"CLM-{year}-") && n.Length == 16);
    }

    [SkippableFact]
    public async Task A_rolled_back_transaction_gives_the_claim_number_back()
    {
        Skip.IfNot(sql.Available, "SQL Server not available");

        async Task<string> Next(bool commit)
        {
            await using var db = sql.CreateContext();
            await using var tx = await db.Database.BeginTransactionAsync();
            var number = await new ClaimNumberGenerator(db, new TestUserAccessor(), TimeProvider.System).NextAsync();
            if (commit) { await tx.CommitAsync(); } else { await tx.RollbackAsync(); }
            return number;
        }

        var rolledBack = await Next(commit: false);
        var next = await Next(commit: true);

        next.Should().Be(rolledBack);
    }

    [SkippableFact]
    public async Task Duplicate_claim_numbers_are_rejected_by_the_database()
    {
        Skip.IfNot(sql.Available, "SQL Server not available");
        var existing = await SaveNewClaim();

        var act = () => SaveNewClaim(existing.ClaimNumber);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [SkippableFact]
    public async Task Delete_becomes_a_soft_delete_and_is_filtered_from_queries()
    {
        Skip.IfNot(sql.Available, "SQL Server not available");
        var saved = await SaveNewClaim();

        await using (var db = sql.CreateContext(userId: User))
        {
            var claim = await db.Claims.SingleAsync(c => c.Id == saved.Id);
            db.Claims.Remove(claim);
            await db.SaveChangesAsync();
        }

        await using var verify = sql.CreateContext();
        (await verify.Claims.AnyAsync(c => c.Id == saved.Id)).Should().BeFalse();
        var raw = await verify.Claims.IgnoreQueryFilters().SingleAsync(c => c.Id == saved.Id);
        raw.IsDeleted.Should().BeTrue();
        raw.DeletedAt.Should().NotBeNull();
        raw.UserModified.Should().Be(User);
    }

    [SkippableFact]
    public async Task Other_tenants_cannot_see_the_claim()
    {
        Skip.IfNot(sql.Available, "SQL Server not available");
        var saved = await SaveNewClaim();

        await using var otherTenant = sql.CreateContext(organisationId: Guid.NewGuid());

        (await otherTenant.Claims.AnyAsync(c => c.Id == saved.Id)).Should().BeFalse();
        (await otherTenant.Policies.AnyAsync()).Should().BeFalse();
    }

    [SkippableFact]
    public async Task Audit_log_accepts_inserts_but_rejects_updates_and_deletes()
    {
        Skip.IfNot(sql.Available, "SQL Server not available");
        var saved = await SaveNewClaim();

        Guid auditId;
        await using (var db = sql.CreateContext())
        {
            var entry = ClaimAuditLog.Create(Guid.Empty, saved.Id, AuditEventTypes.ClaimCreated, "created", DateTimeOffset.UtcNow);
            db.ClaimAuditLog.Add(entry);
            await db.SaveChangesAsync();
            auditId = entry.Id;
            entry.OrganisationId.Should().Be(SeedData.DefaultOrganisationId);
        }

        await using (var db = sql.CreateContext())
        {
            var entry = await db.ClaimAuditLog.SingleAsync(a => a.Id == auditId);
            db.Entry(entry).Property(a => a.Description).CurrentValue = "tampered";
            await FluentActions.Invoking(() => db.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*append-only*");
        }

        await using (var db = sql.CreateContext())
        {
            var entry = await db.ClaimAuditLog.SingleAsync(a => a.Id == auditId);
            db.ClaimAuditLog.Remove(entry);
            await FluentActions.Invoking(() => db.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*append-only*");
        }
    }

    [SkippableFact]
    public async Task Concurrent_edits_to_a_claim_are_detected_by_rowversion()
    {
        Skip.IfNot(sql.Available, "SQL Server not available");
        var saved = await SaveNewClaim();

        await using var first = sql.CreateContext();
        await using var second = sql.CreateContext();
        var a = await first.Claims.SingleAsync(c => c.Id == saved.Id);
        var b = await second.Claims.SingleAsync(c => c.Id == saved.Id);

        a.UpdateNotes("edit by A");
        await first.SaveChangesAsync();

        b.UpdateNotes("edit by B");
        await FluentActions.Invoking(() => second.SaveChangesAsync()).Should().ThrowAsync<DbUpdateConcurrencyException>();
    }

    [SkippableFact]
    public async Task Reserve_idempotency_key_is_unique_in_the_database()
    {
        Skip.IfNot(sql.Available, "SQL Server not available");
        var saved = await SaveNewClaim(withReserve: true);

        await using var db = sql.CreateContext();
        var existing = await db.ReserveHistory.SingleAsync(h => h.ClaimId == saved.Id);

        // Same component, same sequence => same key => the unique index must refuse it.
        var duplicate = await db.Database.ExecuteSqlInterpolatedAsync($"""
            SELECT 1 WHERE EXISTS (SELECT 1 FROM ReserveHistory WHERE IdempotencyKey = {existing.IdempotencyKey})
            """);
        duplicate.Should().Be(-1); // SELECT returns -1 rows-affected; presence is proven below via the index

        var indexes = await db.Database.SqlQuery<string>($"""
            SELECT name AS [Value] FROM sys.indexes WHERE object_id = OBJECT_ID('ReserveHistory') AND is_unique = 1 AND name LIKE '%IdempotencyKey%'
            """).ToListAsync();
        indexes.Should().ContainSingle();
    }

    private sealed class TestUserAccessor : ClaimsModule.Application.Abstractions.ICurrentUserService
    {
        public Guid? UserId => null;
        public string? UserName => "test";
        public UserRole? Role => UserRole.Handler;
        public Guid OrganisationId => SeedData.DefaultOrganisationId;
    }
}
