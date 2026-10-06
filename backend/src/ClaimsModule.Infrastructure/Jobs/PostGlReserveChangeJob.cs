using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Entities;
using Hangfire;
using Hangfire.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClaimsModule.Infrastructure.Jobs;

/// <summary>
/// Simulates posting an approved reserve change to the general ledger (FRS §12.1). Safe to run any number of times:
/// the transaction's idempotency key / posting status is checked before any write, and the posting status is a
/// concurrency token, so two concurrent executions cannot both write the audit entry.
/// </summary>
public sealed class PostGlReserveChangeJob(IServiceScopeFactory scopes, ILogger<PostGlReserveChangeJob> logger)
{
    public const int MaxRetries = 3;

    /// <summary>Hangfire entry point. <paramref name="context"/> is injected by Hangfire (pass null when enqueueing).</summary>
    [AutomaticRetry(Attempts = MaxRetries, DelaysInSeconds = new[] { 10, 60, 300 }, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
    public Task Execute(Guid organisationId, Guid claimId, Guid reserveHistoryId, string idempotencyKey, PerformContext? context) =>
        RunAsync(
            new GlPostingRequest(organisationId, claimId, reserveHistoryId, idempotencyKey),
            context?.BackgroundJob.Id,
            isFinalAttempt: context is not null && context.GetJobParameter<int>("RetryCount") >= MaxRetries,
            CancellationToken.None);

    /// <summary>The job body, separated from Hangfire so it can be exercised directly.</summary>
    public async Task<GlPostingOutcome> RunAsync(GlPostingRequest request, string? jobId, bool isFinalAttempt, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            return await PostAsync(scope.ServiceProvider, request, jobId, ct);
        }
        catch (Exception ex) when (isFinalAttempt)
        {
            logger.LogError(ex, "GL posting {Key} failed after all retries", request.IdempotencyKey);
            await RecordFailureAsync(request, ex, ct);
            throw;
        }
    }

    private async Task<GlPostingOutcome> PostAsync(IServiceProvider services, GlPostingRequest request, string? jobId, CancellationToken ct)
    {
        services.GetRequiredService<BackgroundExecutionContext>().OrganisationId = request.OrganisationId;
        var claims = services.GetRequiredService<IClaimRepository>();
        var uow = services.GetRequiredService<IUnitOfWork>();
        var audit = services.GetRequiredService<IAuditLogService>();

        return await uow.ExecuteInTransactionAsync(async token =>
        {
            var claim = await claims.GetByIdAsync(request.ClaimId, ClaimIncludes.Reserves, track: true, token);
            var component = claim?.ReserveComponents.FirstOrDefault(c => c.History.Any(h => h.Id == request.ReserveHistoryId));
            var txn = component?.History.First(h => h.Id == request.ReserveHistoryId);

            if (claim is null || component is null || txn is null)
            {
                logger.LogWarning("GL posting skipped: reserve transaction {Id} not found", request.ReserveHistoryId);
                return GlPostingOutcome.NotFound;
            }

            if (txn.IdempotencyKey != request.IdempotencyKey)
            {
                logger.LogWarning("GL posting skipped: key mismatch for {Id}", request.ReserveHistoryId);
                return GlPostingOutcome.NotFound;
            }

            if (!txn.IsApproved)
            {
                logger.LogWarning("GL posting skipped: {Key} is not approved", request.IdempotencyKey);
                return GlPostingOutcome.NotApproved;
            }

            // Idempotency check before any write: a repeated or retried run finds it already posted and does nothing.
            if (!txn.TryMarkPosted(jobId))
            {
                logger.LogInformation("GL posting {Key} already posted; nothing to do", request.IdempotencyKey);
                return GlPostingOutcome.AlreadyPosted;
            }

            var entry = GlJournal.For(component.Component.ToString(), txn.Amount);
            audit.Record(
                claim.Id, AuditEventTypes.GlPostingSimulated,
                $"Simulated GL posting: {entry.Description}, Amount = {entry.Amount:N2}",
                newValue: new
                {
                    entry.Journal, entry.DebitAccount, entry.CreditAccount, entry.Amount,
                    Component = component.Component, txn.ChangeSequence, request.IdempotencyKey,
                },
                relatedEntityId: txn.Id, relatedEntityType: nameof(ReserveHistory));

            await uow.SaveChangesAsync(token);
            return GlPostingOutcome.Posted;
        }, ct);
    }

    /// <summary>Marks the transaction Failed and writes GL_POSTING_FAILED in a fresh scope (the failed one is unusable).</summary>
    private async Task RecordFailureAsync(GlPostingRequest request, Exception failure, CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            services.GetRequiredService<BackgroundExecutionContext>().OrganisationId = request.OrganisationId;
            var claims = services.GetRequiredService<IClaimRepository>();
            var uow = services.GetRequiredService<IUnitOfWork>();
            var audit = services.GetRequiredService<IAuditLogService>();

            await uow.ExecuteInTransactionAsync(async token =>
            {
                var claim = await claims.GetByIdAsync(request.ClaimId, ClaimIncludes.Reserves, track: true, token);
                var txn = claim?.ReserveComponents.SelectMany(c => c.History).FirstOrDefault(h => h.Id == request.ReserveHistoryId);
                if (claim is null || txn is null || txn.PostingStatus == Domain.Enums.ReservePostingStatus.Posted)
                {
                    return false;
                }

                txn.MarkPostingFailed();
                audit.Record(
                    claim.Id, AuditEventTypes.GlPostingFailed,
                    $"Simulated GL posting failed after {MaxRetries} retries: {failure.Message}",
                    newValue: new { Reason = failure.Message, request.IdempotencyKey },
                    relatedEntityId: txn.Id, relatedEntityType: nameof(ReserveHistory));
                await uow.SaveChangesAsync(token);
                return true;
            }, ct);
        }
        catch (Exception recordingFailure)
        {
            // Never mask the original failure; Hangfire will still mark the job Failed.
            logger.LogError(recordingFailure, "Could not record GL posting failure for {Key}", request.IdempotencyKey);
        }
    }
}

public sealed record GlPostingRequest(Guid OrganisationId, Guid ClaimId, Guid ReserveHistoryId, string IdempotencyKey);

public enum GlPostingOutcome { Posted, AlreadyPosted, NotFound, NotApproved }

/// <summary>
/// An increase books DR Change in Outstanding Reserves / CR Outstanding Loss Reserves (FRS §6.5); a decrease books the
/// mirror entry, so the amount in the journal is always positive.
/// </summary>
public sealed record GlJournal(string Journal, string DebitAccount, string CreditAccount, decimal Amount)
{
    public const string ChangeInReserves = "Change in Outstanding Reserves";
    public const string OutstandingLossReserves = "Outstanding Loss Reserves";

    public string Description => $"DR {DebitAccount} / CR {CreditAccount}";

    public static GlJournal For(string component, decimal delta) => delta >= 0
        ? new GlJournal($"{component} reserve increase", ChangeInReserves, OutstandingLossReserves, delta)
        : new GlJournal($"{component} reserve decrease", OutstandingLossReserves, ChangeInReserves, -delta);
}
