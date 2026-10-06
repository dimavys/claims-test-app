using ClaimsModule.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClaimsModule.Persistence.Configurations;

public sealed class ClaimAuditLogConfiguration : IEntityTypeConfiguration<ClaimAuditLog>
{
    public void Configure(EntityTypeBuilder<ClaimAuditLog> b)
    {
        b.ToTable("ClaimAuditLog");

        b.HasKey(a => a.Id);
        b.Property(a => a.Id).HasColumnName("AuditLogId").HasDefaultValueSql("NEWSEQUENTIALID()").ValueGeneratedNever();

        b.Property(a => a.OrganisationId).IsRequired();
        b.Property(a => a.ClaimId).IsRequired();
        b.Property(a => a.EventType).HasMaxLength(100).IsRequired();
        b.Property(a => a.Description).IsRequired(); // NVARCHAR(MAX)
        b.Property(a => a.OldValue);
        b.Property(a => a.NewValue);
        b.Property(a => a.RelatedEntityId);
        b.Property(a => a.RelatedEntityType).HasMaxLength(100);
        b.Property(a => a.CorrelationId);
        b.Property(a => a.CreatedAt).IsRequired();
        b.Property(a => a.CreatedByUserId);

        b.HasIndex(a => new { a.ClaimId, a.CreatedAt });
        b.HasIndex(a => new { a.ClaimId, a.EventType, a.CreatedAt }); // SLA job: last breach per claim
        b.HasIndex(a => a.OrganisationId);

        b.HasOne<Claim>().WithMany().HasForeignKey(a => a.ClaimId).OnDelete(DeleteBehavior.Restrict);
    }
}
