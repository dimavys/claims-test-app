using ClaimsModule.Domain.Entities;
using ClaimsModule.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClaimsModule.Persistence.Configurations;

public sealed class ClaimConfiguration : BaseEntityConfiguration<Claim>
{
    protected override string TableName => "Claims";
    protected override string KeyColumn => "ClaimId";

    protected override void ConfigureEntity(EntityTypeBuilder<Claim> b)
    {
        b.Property(c => c.ClaimNumber).HasMaxLength(50).IsRequired();
        b.Property(c => c.PolicyNumber).HasMaxLength(50);
        b.Property(c => c.ClientName).HasMaxLength(255).IsRequired();
        b.Property(c => c.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        b.Property(c => c.Severity).HasConversion<string>().HasMaxLength(50).IsRequired();
        b.Property(c => c.ClaimType).HasMaxLength(50);
        b.Property(c => c.ReportedDate).IsRequired();
        b.Property(c => c.ClosureReason).HasMaxLength(500);
        b.Property(c => c.Notes); // NVARCHAR(MAX)
        b.Property(c => c.ManagerOverride).IsRequired().HasDefaultValue(false);

        // Unique per organisation, soft-deleted rows included: a number is never reused (BR-C-04).
        b.HasIndex(c => new { c.OrganisationId, c.ClaimNumber }).IsUnique();
        b.HasIndex(c => c.Status);
        b.HasIndex(c => c.ReportedDate);
        b.HasIndex(c => c.PolicyId);
        b.HasIndex(c => c.AssignedHandlerId);

        b.HasOne<Policy>().WithMany().HasForeignKey(c => c.PolicyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(c => c.LossEvent).WithOne().HasForeignKey<LossEvent>(l => l.ClaimId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(c => c.Parties).WithOne().HasForeignKey(p => p.ClaimId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(c => c.RiskObjects).WithOne().HasForeignKey(r => r.ClaimId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(c => c.ReserveComponents).WithOne().HasForeignKey(r => r.ClaimId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(c => c.Documents).WithOne().HasForeignKey(d => d.ClaimId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(c => c.ValidationIssues).WithOne().HasForeignKey(i => i.ClaimId).OnDelete(DeleteBehavior.Restrict);

        b.Navigation(c => c.Parties).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(c => c.RiskObjects).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(c => c.ReserveComponents).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(c => c.Documents).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(c => c.ValidationIssues).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class LossEventConfiguration : BaseEntityConfiguration<LossEvent>
{
    protected override string TableName => "LossEvents";
    protected override string KeyColumn => "LossEventId";

    protected override void ConfigureEntity(EntityTypeBuilder<LossEvent> b)
    {
        b.Property(l => l.LossDate).IsRequired();
        b.Property(l => l.LossDescription).IsRequired(); // NVARCHAR(MAX)
        b.Property(l => l.LossLocation).HasMaxLength(500);
        b.Property(l => l.CauseOfLossCode).HasMaxLength(50).IsRequired();
        b.Property(l => l.EstimatedLossAmount);
        b.Property(l => l.ReportDate).IsRequired();
        b.Property(l => l.PoliceReportNumber).HasMaxLength(100);

        b.HasIndex(l => l.ClaimId).IsUnique(); // one loss event per claim
        b.HasIndex(l => l.CauseOfLossCode);

        // FK to the natural key CauseOfLossCodes.Code (FRS §9.2).
        b.HasOne<CauseOfLossCode>().WithMany().HasForeignKey(l => l.CauseOfLossCode)
            .HasPrincipalKey(c => c.Code).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ClaimPartyConfiguration : BaseEntityConfiguration<ClaimParty>
{
    protected override string TableName => "ClaimParties";
    protected override string KeyColumn => "ClaimPartyId";

    protected override void ConfigureEntity(EntityTypeBuilder<ClaimParty> b)
    {
        b.Property(p => p.PartyRole).HasConversion<string>().HasMaxLength(50).IsRequired();
        b.Property(p => p.PartyType).HasConversion<string>().HasMaxLength(20).IsRequired();
        b.Property(p => p.FirstName).HasMaxLength(100);
        b.Property(p => p.LastName).HasMaxLength(100);
        b.Property(p => p.CompanyName).HasMaxLength(255);
        b.Property(p => p.Email).HasMaxLength(255);
        b.Property(p => p.Phone).HasMaxLength(50);
        b.Property(p => p.Notes);
        b.Property(p => p.IsActive).IsRequired().HasDefaultValue(true);
        b.Ignore(p => p.DisplayName);

        b.HasIndex(p => new { p.ClaimId, p.PartyRole });
    }
}

public sealed class ClaimRiskObjectConfiguration : BaseEntityConfiguration<ClaimRiskObject>
{
    protected override string TableName => "ClaimRiskObjects";
    protected override string KeyColumn => "ClaimRiskObjectId";

    protected override void ConfigureEntity(EntityTypeBuilder<ClaimRiskObject> b)
    {
        b.Property(r => r.AssetType).HasConversion<string>().HasMaxLength(50).IsRequired();
        b.Property(r => r.AssetDescription).HasMaxLength(500).IsRequired();
        b.Property(r => r.DamageDescription);
        b.Property(r => r.IsPrimary).IsRequired().HasDefaultValue(false);
        b.Property(r => r.AssetReference).HasMaxLength(255);

        b.HasIndex(r => r.ClaimId);
    }
}

public sealed class ClaimReserveComponentConfiguration : BaseEntityConfiguration<ClaimReserveComponent>
{
    protected override string TableName => "ClaimReserveComponents";
    protected override string KeyColumn => "ReserveComponentId";

    protected override void ConfigureEntity(EntityTypeBuilder<ClaimReserveComponent> b)
    {
        b.Property(r => r.Component).HasConversion<string>().HasMaxLength(50).IsRequired();
        b.Property(r => r.CurrentAmount).IsRequired();
        b.Property(r => r.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        b.Property(r => r.Notes);
        b.Ignore(r => r.AllowsNegativeBalance);
        b.Ignore(r => r.PendingAmount);

        // One reserve line per component type per claim.
        b.HasIndex(r => new { r.ClaimId, r.Component }).IsUnique();

        b.HasMany(r => r.History).WithOne().HasForeignKey(h => h.ReserveComponentId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(r => r.History).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class ReserveHistoryConfiguration : BaseEntityConfiguration<ReserveHistory>
{
    protected override string TableName => "ReserveHistory";
    protected override string KeyColumn => "ReserveHistoryId";

    protected override void ConfigureEntity(EntityTypeBuilder<ReserveHistory> b)
    {
        b.Property(h => h.TransactionType).HasConversion<string>().HasMaxLength(50).IsRequired();
        b.Property(h => h.Amount).IsRequired();
        b.Property(h => h.PreviousBalance).IsRequired();
        b.Property(h => h.NewBalance).IsRequired();
        // Lifecycle columns are concurrency tokens: two approvers (or an approver racing a retract) cannot both win,
        // and a duplicated GL job cannot post the same transaction twice.
        b.Property(h => h.ApprovalStatus).HasConversion<string>().HasMaxLength(50).IsRequired().IsConcurrencyToken();
        b.Property(h => h.RejectionReason);
        b.Property(h => h.ChangeReason).HasMaxLength(500).IsRequired();
        b.Property(h => h.PostingStatus).HasConversion<string>().HasMaxLength(50).IsRequired().IsConcurrencyToken();
        b.Property(h => h.PostingJobId).HasMaxLength(100);
        b.Property(h => h.IdempotencyKey).HasMaxLength(200).IsRequired();
        b.Property(h => h.ChangeSequence).IsRequired();
        b.Ignore(h => h.IsApproved);
        b.Ignore(h => h.IsPending);

        // Idempotency guarantees for the GL job (BR-R-06) are backed by the database.
        b.HasIndex(h => h.IdempotencyKey).IsUnique();
        b.HasIndex(h => new { h.ReserveComponentId, h.ChangeSequence }).IsUnique();
        b.HasIndex(h => new { h.ClaimId, h.ApprovalStatus });

        // Denormalised ClaimId (FRS §9.6); NO ACTION avoids multiple cascade paths.
        b.HasOne<Claim>().WithMany().HasForeignKey(h => h.ClaimId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ClaimDocumentConfiguration : BaseEntityConfiguration<ClaimDocument>
{
    protected override string TableName => "ClaimDocuments";
    protected override string KeyColumn => "ClaimDocumentId";

    protected override void ConfigureEntity(EntityTypeBuilder<ClaimDocument> b)
    {
        b.Property(d => d.DocumentType).HasMaxLength(100).IsRequired();
        b.Property(d => d.DocumentName).HasMaxLength(255).IsRequired();
        b.Property(d => d.BlobPath).HasMaxLength(500).IsRequired();
        b.Property(d => d.ContentType).HasMaxLength(100).IsRequired();
        b.Property(d => d.FileSizeBytes).IsRequired();
        b.Property(d => d.UploadedAt).IsRequired();
        b.Property(d => d.Notes).HasMaxLength(500);

        b.HasIndex(d => d.ClaimId);
    }
}

public sealed class ClaimValidationIssueConfiguration : BaseEntityConfiguration<ClaimValidationIssue>
{
    protected override string TableName => "ClaimValidationIssues";
    protected override string KeyColumn => "ClaimValidationIssueId";

    protected override void ConfigureEntity(EntityTypeBuilder<ClaimValidationIssue> b)
    {
        b.Property(i => i.Severity).HasConversion<string>().HasMaxLength(50).IsRequired();
        b.Property(i => i.Code).HasMaxLength(50).IsRequired();
        b.Property(i => i.Field).HasMaxLength(100);
        b.Property(i => i.Message).HasMaxLength(1000).IsRequired();
        b.Property(i => i.RequiresAcknowledgement).IsRequired().HasDefaultValue(false);
        b.Property(i => i.IsResolved).IsRequired().HasDefaultValue(false);
        b.Property(i => i.IsAcknowledged).IsRequired().HasDefaultValue(false);
        b.Ignore(i => i.IsOutstanding);

        b.HasIndex(i => new { i.ClaimId, i.IsResolved });
    }
}

public sealed class CauseOfLossCodeConfiguration : BaseEntityConfiguration<CauseOfLossCode>
{
    protected override string TableName => "CauseOfLossCodes";
    protected override string KeyColumn => "CauseOfLossCodeId";

    protected override void ConfigureEntity(EntityTypeBuilder<CauseOfLossCode> b)
    {
        b.Property(c => c.Code).HasMaxLength(50).IsRequired();
        b.Property(c => c.Name).HasMaxLength(255).IsRequired();
        b.Property(c => c.PerilCategory).HasConversion<string>().HasMaxLength(50).IsRequired();
        b.Property(c => c.IsActive).IsRequired().HasDefaultValue(true);
        b.Property(c => c.SortOrder).IsRequired();

        b.HasAlternateKey(c => c.Code); // target of LossEvents.CauseOfLossCode
        b.HasData(SeedData.CauseOfLossCodes());
    }
}

public sealed class PolicyConfiguration : BaseEntityConfiguration<Policy>
{
    protected override string TableName => "Policies";
    protected override string KeyColumn => "PolicyId";

    protected override void ConfigureEntity(EntityTypeBuilder<Policy> b)
    {
        b.Property(p => p.PolicyNumber).HasMaxLength(50).IsRequired();
        b.Property(p => p.ClientName).HasMaxLength(255).IsRequired();
        b.Property(p => p.EffectiveDate).IsRequired();
        b.Property(p => p.ExpirationDate).IsRequired();
        b.Property(p => p.Status).HasConversion<string>().HasMaxLength(50).IsRequired();
        b.Property(p => p.CoverageTypes).HasMaxLength(500).IsRequired();

        b.HasIndex(p => p.PolicyNumber).IsUnique();
        b.HasIndex(p => p.ClientName);
        b.HasData(SeedData.Policies());
    }
}

public sealed class ClaimStatusTransitionConfiguration : BaseEntityConfiguration<ClaimStatusTransition>
{
    protected override string TableName => "ClaimStatusTransitions";
    protected override string KeyColumn => "ClaimStatusTransitionId";

    protected override void ConfigureEntity(EntityTypeBuilder<ClaimStatusTransition> b)
    {
        b.Property(t => t.FromStatus).HasConversion<string>().HasMaxLength(50).IsRequired();
        b.Property(t => t.ToStatus).HasConversion<string>().HasMaxLength(50).IsRequired();
        b.Property(t => t.RequiredPermission).HasConversion<string>().HasMaxLength(50).IsRequired();
        b.Property(t => t.Description).HasMaxLength(500);

        b.HasIndex(t => new { t.OrganisationId, t.FromStatus, t.ToStatus }).IsUnique();
        b.HasData(SeedData.StatusTransitions());
    }
}

public sealed class ClaimNumberSequenceConfiguration : BaseEntityConfiguration<ClaimNumberSequence>
{
    protected override string TableName => "ClaimNumberSequences";
    protected override string KeyColumn => "ClaimNumberSequenceId";

    protected override void ConfigureEntity(EntityTypeBuilder<ClaimNumberSequence> b)
    {
        b.Property(s => s.Year).IsRequired();
        b.Property(s => s.LastValue).IsRequired();
        b.HasIndex(s => new { s.OrganisationId, s.Year }).IsUnique();
    }
}
