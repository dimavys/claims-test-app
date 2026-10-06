using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Entities;
using ClaimsModule.Infrastructure.Options;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ClaimsModule.Infrastructure.Jobs;

/// <summary>
/// Recurring scan (every 15 minutes) for Draft/Open claims not updated in 48 hours (FRS §12.2). It only writes
/// SLA_BREACH_DETECTED audit entries, at most one per claim per 24 hours, and never changes the claim itself.
/// </summary>
public sealed class SlaMonitoringJob(
    IServiceScopeFactory scopes, IOptions<TenantOptions> tenant, TimeProvider time, ILogger<SlaMonitoringJob> logger)
{
    public const string JobId = "sla-monitoring";
    public const string CronEvery15Minutes = "*/15 * * * *";
    public static readonly TimeSpan StaleAfter = TimeSpan.FromHours(48);
    public static readonly TimeSpan RepeatEvery = TimeSpan.FromHours(24);

    /// <summary>Hangfire entry point; overlapping runs are prevented with a distributed lock.</summary>
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task Execute() => await RunAsync(CancellationToken.None);

    /// <returns>The number of breach entries written.</returns>
    public async Task<int> RunAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<BackgroundExecutionContext>().OrganisationId = tenant.Value.OrganisationId;
        var claims = services.GetRequiredService<IClaimRepository>();
        var auditRepository = services.GetRequiredService<IAuditLogRepository>();
        var audit = services.GetRequiredService<IAuditLogService>();
        var uow = services.GetRequiredService<IUnitOfWork>();

        var written = await uow.ExecuteInTransactionAsync(async token =>
        {
            var now = time.GetUtcNow();
            var stale = await claims.GetStaleClaimsAsync(now - StaleAfter, token);
            var count = 0;

            foreach (var claim in stale)
            {
                var last = await auditRepository.GetLatestAsync(claim.Id, AuditEventTypes.SlaBreachDetected, token);
                if (last is not null && now - last.CreatedAt < RepeatEvery)
                {
                    continue; // already flagged within the last 24 hours
                }

                var lastActivity = claim.UpdatedAt ?? claim.CreatedAt;
                audit.Record(
                    claim.Id, AuditEventTypes.SlaBreachDetected, "Claim has not been updated in 48 hours",
                    newValue: new { claim.Status, LastUpdatedAt = lastActivity, HoursSinceUpdate = Math.Round((now - lastActivity).TotalHours, 1) },
                    relatedEntityId: claim.Id, relatedEntityType: nameof(Claim));
                count++;
            }

            await uow.SaveChangesAsync(token);
            return count;
        }, ct);

        logger.LogInformation("SLA monitoring flagged {Count} claim(s)", written);
        return written;
    }
}
