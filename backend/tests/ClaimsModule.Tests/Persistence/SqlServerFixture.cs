using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Persistence;
using ClaimsModule.Persistence.Seed;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ClaimsModule.Tests.Persistence;

/// <summary>
/// Creates a throw-away database on the local SQL Server (docker compose), applies the real migrations,
/// and drops it afterwards. Tests are skipped when SQL Server is not reachable.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly string _databaseName = "ClaimsTest_" + Guid.NewGuid().ToString("N")[..12];
    private string _connectionString = string.Empty;

    public bool Available { get; private set; }
    public string ConnectionString => _connectionString;

    public async Task InitializeAsync()
    {
        var baseConnection = Environment.GetEnvironmentVariable("ConnectionStrings__ClaimsDb")
            ?? "Server=localhost,1433;Database=ClaimsDb;User Id=sa;Password=Claims_Dev#2026;TrustServerCertificate=True;Encrypt=False";

        var builder = new SqlConnectionStringBuilder(baseConnection) { InitialCatalog = _databaseName, ConnectTimeout = 5 };
        _connectionString = builder.ConnectionString;

        try
        {
            await using var context = CreateContext();
            await context.Database.MigrateAsync();
            Available = true;
        }
        catch (Exception)
        {
            Available = false;
        }
    }

    public async Task DisposeAsync()
    {
        if (!Available)
        {
            return;
        }

        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }

    public ClaimsDbContext CreateContext(Guid? organisationId = null, Guid? userId = null)
    {
        var options = new DbContextOptionsBuilder<ClaimsDbContext>().UseSqlServer(_connectionString).Options;
        return new ClaimsDbContext(options, new TestUser(organisationId ?? SeedData.DefaultOrganisationId, userId), TimeProvider.System);
    }

    private sealed class TestUser(Guid organisationId, Guid? userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public string? UserName => "test";
        public UserRole? Role => UserRole.Handler;
        public Guid OrganisationId => organisationId;
    }
}

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
