using ClaimsModule.Application;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Infrastructure.Jobs;
using ClaimsModule.Infrastructure.Options;
using ClaimsModule.Persistence;
using ClaimsModule.Persistence.Seed;
using ClaimsModule.Tests.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClaimsModule.Tests.Application;

public sealed record TestUser(Guid Id, string Name, UserRole Role);

internal static class Users
{
    public static readonly TestUser Handler = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"), "Hannah Handler", UserRole.Handler);
    public static readonly TestUser Handler2 = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004"), "Harry Handler", UserRole.Handler);
    public static readonly TestUser Supervisor = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002"), "Sam Supervisor", UserRole.Supervisor);
    public static readonly TestUser Manager = new(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"), "Maria Manager", UserRole.Manager);
    public static readonly TestUser[] All = { Handler, Handler2, Supervisor, Manager };
}

internal sealed class UserHolder
{
    public TestUser? User { get; set; }
}

internal sealed class ScopedUser(UserHolder holder) : ICurrentUserService
{
    public Guid? UserId => holder.User?.Id;
    public string? UserName => holder.User?.Name;
    public UserRole? Role => holder.User?.Role;
    public Guid OrganisationId => SeedData.DefaultOrganisationId;
}

internal sealed class FakeUserDirectory : IUserDirectory
{
    public string? GetDisplayName(Guid? userId) => Users.All.FirstOrDefault(u => u.Id == userId)?.Name;

    public IReadOnlyList<Guid> FindByName(string text) =>
        Users.All.Where(u => u.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).Select(u => u.Id).ToList();
}

internal sealed class FakeStorage : IStorageService
{
    public Dictionary<string, (byte[] Bytes, string ContentType)> Files { get; } = new();

    public async Task<string> UploadAsync(string relativePath, Stream content, string contentType, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        var stored = "claim-documents/" + relativePath;
        Files[stored] = (ms.ToArray(), contentType);
        return stored;
    }

    public Task<Uri> GetDownloadUrlAsync(string storedPath, TimeSpan timeToLive, CancellationToken ct = default) =>
        Task.FromResult(new Uri($"https://storage.test/{storedPath}?sig=fake&ttl={(int)timeToLive.TotalSeconds}"));
}

internal sealed class RecordingScheduler : IBackgroundJobScheduler
{
    public List<(Guid ClaimId, Guid ReserveHistoryId, string Key)> Enqueued { get; } = new();

    public void EnqueueGlPosting(Guid organisationId, Guid claimId, Guid reserveHistoryId, string idempotencyKey) =>
        Enqueued.Add((claimId, reserveHistoryId, idempotencyKey));
}

internal sealed class FakeCorrelation : ICorrelationContext
{
    public Guid CorrelationId { get; } = Guid.NewGuid();
}

/// <summary>Real MediatR pipeline + real SQL Server; only storage, jobs and the user directory are faked.</summary>
internal class AppHarness : IDisposable
{
    private readonly ServiceProvider _root;
    private readonly SqlServerFixture _sql;

    public FakeStorage Storage { get; } = new();
    public RecordingScheduler Scheduler { get; } = new();

    public AppHarness(SqlServerFixture sql)
    {
        _sql = sql;
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:ClaimsDb"] = sql.ConnectionString })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddApplication();
        services.AddPersistence(config);
        services.AddScoped<UserHolder>();
        services.AddScoped<ICurrentUserService, ScopedUser>();
        services.AddScoped<ICorrelationContext, FakeCorrelation>();
        services.AddSingleton<IUserDirectory, FakeUserDirectory>();
        services.AddSingleton<IStorageService>(Storage);
        services.AddSingleton<IBackgroundJobScheduler>(Scheduler);

        // Background jobs under test (run directly, not through a Hangfire server).
        services.Configure<TenantOptions>(o => o.OrganisationId = SeedData.DefaultOrganisationId);
        services.AddScoped<BackgroundExecutionContext>();
        _root = BuildRoot(services);
    }

    protected virtual ServiceProvider BuildRoot(IServiceCollection services)
    {
        services.AddTransient<PostGlReserveChangeJob>();
        services.AddTransient<SlaMonitoringJob>();
        return services.BuildServiceProvider();
    }

    public IServiceProvider Root => _root;

    public async Task<T> Send<T>(IRequest<T> request, TestUser? user = null)
    {
        await using var scope = _root.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserHolder>().User = user ?? Users.Handler;
        return await scope.ServiceProvider.GetRequiredService<IMediator>().Send(request);
    }

    public async Task<T> WithScope<T>(Func<IServiceProvider, Task<T>> action, TestUser? user = null)
    {
        await using var scope = _root.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<UserHolder>().User = user ?? Users.Handler;
        return await action(scope.ServiceProvider);
    }

    public void Dispose() => _root.Dispose();
}

internal static class HarnessSql
{
    /// <summary>Makes a claim look untouched for <paramref name="hours"/> hours (UpdatedAt cleared, CreatedAt moved back).</summary>
    public static async Task BackdateClaimAsync(this SqlServerFixture sql, Guid claimId, double hours)
    {
        await using var db = sql.CreateContext();
        var when = DateTimeOffset.UtcNow.AddHours(-hours);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Claims SET CreatedAt = {when}, UpdatedAt = NULL WHERE ClaimId = {claimId}");
    }

    public static async Task BackdateAuditAsync(this SqlServerFixture sql, Guid claimId, string eventType, double hours)
    {
        await using var db = sql.CreateContext();
        var when = DateTimeOffset.UtcNow.AddHours(-hours);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ClaimAuditLog SET CreatedAt = {when} WHERE ClaimId = {claimId} AND EventType = {eventType}");
    }
}
