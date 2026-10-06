using ClaimsModule.Application.Abstractions;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace ClaimsModule.Infrastructure.Jobs;

public sealed class HangfireJobScheduler(IBackgroundJobClient client) : IBackgroundJobScheduler
{
    public void EnqueueGlPosting(Guid organisationId, Guid claimId, Guid reserveHistoryId, string idempotencyKey) =>
        client.Enqueue<PostGlReserveChangeJob>(j => j.Execute(organisationId, claimId, reserveHistoryId, idempotencyKey, null));
}

/// <summary>Used when background processing is switched off (e.g. tooling or tests): jobs are logged, not run.</summary>
public sealed class NullJobScheduler(ILogger<NullJobScheduler> logger) : IBackgroundJobScheduler
{
    public void EnqueueGlPosting(Guid organisationId, Guid claimId, Guid reserveHistoryId, string idempotencyKey) =>
        logger.LogWarning("Background jobs are disabled; GL posting {Key} was not queued", idempotencyKey);
}
