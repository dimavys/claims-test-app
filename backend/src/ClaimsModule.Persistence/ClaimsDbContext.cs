using System.Reflection;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Common;
using ClaimsModule.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Persistence;

public class ClaimsDbContext : DbContext
{
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _time;

    public ClaimsDbContext(DbContextOptions<ClaimsDbContext> options, ICurrentUserService currentUser, TimeProvider time)
        : base(options)
    {
        _currentUser = currentUser;
        _time = time;
    }

    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<LossEvent> LossEvents => Set<LossEvent>();
    public DbSet<ClaimParty> ClaimParties => Set<ClaimParty>();
    public DbSet<ClaimRiskObject> ClaimRiskObjects => Set<ClaimRiskObject>();
    public DbSet<ClaimReserveComponent> ClaimReserveComponents => Set<ClaimReserveComponent>();
    public DbSet<ReserveHistory> ReserveHistory => Set<ReserveHistory>();
    public DbSet<ClaimDocument> ClaimDocuments => Set<ClaimDocument>();
    public DbSet<ClaimAuditLog> ClaimAuditLog => Set<ClaimAuditLog>();
    public DbSet<ClaimValidationIssue> ClaimValidationIssues => Set<ClaimValidationIssue>();
    public DbSet<CauseOfLossCode> CauseOfLossCodes => Set<CauseOfLossCode>();
    public DbSet<Policy> Policies => Set<Policy>();
    public DbSet<ClaimStatusTransition> ClaimStatusTransitions => Set<ClaimStatusTransition>();
    public DbSet<ClaimNumberSequence> ClaimNumberSequences => Set<ClaimNumberSequence>();

    /// <summary>Evaluated per context instance by the global query filters.</summary>
    private Guid CurrentOrganisationId => _currentUser.OrganisationId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ClaimsDbContext).Assembly);

        // Every entity is tenant-scoped; every BaseEntity is also soft-deletable.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes().Select(t => t.ClrType).ToList())
        {
            var method = entityType.IsAssignableTo(typeof(BaseEntity))
                ? nameof(ApplySoftDeleteAndTenantFilter)
                : entityType.IsAssignableTo(typeof(ITenantEntity)) ? nameof(ApplyTenantFilter) : null;

            if (method is not null)
            {
                typeof(ClaimsDbContext)
                    .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(entityType)
                    .Invoke(this, new object[] { modelBuilder });
            }
        }
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Monetary amounts are always DECIMAL(19,4) (FRS §15.1).
        configurationBuilder.Properties<decimal>().HavePrecision(19, 4);
    }

    private void ApplySoftDeleteAndTenantFilter<T>(ModelBuilder modelBuilder) where T : BaseEntity =>
        modelBuilder.Entity<T>().HasQueryFilter(e => !e.IsDeleted && e.OrganisationId == CurrentOrganisationId);

    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder) where T : class, ITenantEntity =>
        modelBuilder.Entity<T>().HasQueryFilter(e => e.OrganisationId == CurrentOrganisationId);

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditing();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditing();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    private void ApplyAuditing()
    {
        var now = _time.GetUtcNow();
        var userId = _currentUser.UserId;
        var organisationId = _currentUser.OrganisationId;

        foreach (var entry in ChangeTracker.Entries().ToList())
        {
            switch (entry.Entity)
            {
                case ClaimAuditLog log:
                    // BR-A-01: the audit log is append-only. The application layer refuses any change or removal.
                    if (entry.State is EntityState.Modified or EntityState.Deleted)
                    {
                        throw new InvalidOperationException("ClaimAuditLog is append-only; updates and deletes are not permitted.");
                    }

                    if (entry.State == EntityState.Added)
                    {
                        log.StampTenant(organisationId);
                    }

                    break;

                case BaseEntity entity:
                    switch (entry.State)
                    {
                        case EntityState.Added:
                            entity.StampCreated(organisationId, now, userId);
                            break;
                        case EntityState.Modified:
                            entity.StampModified(now, userId);
                            break;
                        case EntityState.Deleted:
                            // Soft delete: never issue a physical DELETE.
                            entry.State = EntityState.Modified;
                            entity.MarkDeleted(now, userId);
                            break;
                    }

                    break;
            }
        }
    }
}
