using ClaimsModule.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClaimsModule.Persistence.Configurations;

/// <summary>Applies the column conventions of FRS §15.1 to every table; subclasses add their own columns.</summary>
public abstract class BaseEntityConfiguration<T> : IEntityTypeConfiguration<T> where T : BaseEntity
{
    protected abstract string TableName { get; }
    protected abstract string KeyColumn { get; }

    public void Configure(EntityTypeBuilder<T> builder)
    {
        builder.ToTable(TableName);

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName(KeyColumn)
            .HasDefaultValueSql("NEWSEQUENTIALID()")
            .ValueGeneratedNever(); // Ids are assigned in the domain with an equivalent sequential generator.

        builder.Property(e => e.OrganisationId).IsRequired();
        builder.Property(e => e.IsDeleted).IsRequired().HasDefaultValue(false);
        builder.Property(e => e.DeletedAt);
        builder.Property(e => e.CreatedAt).IsRequired();
        builder.Property(e => e.UpdatedAt);
        builder.Property(e => e.UserCreated);
        builder.Property(e => e.UserModified);

        builder.HasIndex(e => e.OrganisationId);

        if (typeof(AggregateRoot).IsAssignableFrom(typeof(T)))
        {
            builder.Property(nameof(AggregateRoot.RowVer)).IsRowVersion();
            builder.Ignore(nameof(AggregateRoot.DomainEvents));
        }

        ConfigureEntity(builder);
    }

    protected abstract void ConfigureEntity(EntityTypeBuilder<T> builder);
}
