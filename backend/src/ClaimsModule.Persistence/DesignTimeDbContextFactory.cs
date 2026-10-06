using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClaimsModule.Persistence;

/// <summary>Used only by `dotnet ef`; lets migrations be created and applied without booting the API.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ClaimsDbContext>
{
    // Local docker-compose default; override with ConnectionStrings__ClaimsDb.
    private const string LocalDefault =
        "Server=localhost,1433;Database=ClaimsDb;User Id=sa;Password=Claims_Dev#2026;TrustServerCertificate=True;Encrypt=False";

    public ClaimsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ClaimsDb") ?? LocalDefault;
        var options = new DbContextOptionsBuilder<ClaimsDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(ClaimsDbContext).Assembly.FullName))
            .Options;

        return new ClaimsDbContext(options, new DesignTimeUser(), TimeProvider.System);
    }

    private sealed class DesignTimeUser : ICurrentUserService
    {
        public Guid? UserId => null;
        public string? UserName => "design-time";
        public UserRole? Role => null;
        public Guid OrganisationId => SeedData.DefaultOrganisationId;
    }
}
