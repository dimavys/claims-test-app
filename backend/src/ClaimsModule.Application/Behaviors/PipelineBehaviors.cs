using System.Diagnostics;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Common;
using ClaimsModule.Application.DomainEvents;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace ClaimsModule.Application.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var response = await next();
            logger.LogInformation("Handled {Request} in {ElapsedMs:0}ms", name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return response;
        }
        catch (Exception ex) when (ex is RequestValidationException or Domain.Common.DomainException or NotFoundException)
        {
            // Expected business outcomes: log at information level without a stack trace.
            logger.LogInformation("{Request} rejected: {Reason}", name, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "{Request} failed after {ElapsedMs:0}ms", name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }
}

/// <summary>Runs every FluentValidation validator for the request at the MediatR pipeline level (not just the controller).</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (validators.Any())
        {
            var context = new ValidationContext<TRequest>(request);
            var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, ct)));
            var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();

            if (failures.Count > 0)
            {
                var errors = failures
                    .GroupBy(f => f.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).Distinct().ToArray());
                throw new RequestValidationException(errors);
            }
        }

        return await next();
    }
}

/// <summary>
/// Wraps every command in one transaction: handler → dispatch domain events (their handlers add audit rows to the
/// same unit of work) → save → commit → run after-commit actions (e.g. enqueue Hangfire jobs).
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse>(
    IUnitOfWork unitOfWork,
    IPublisher publisher,
    IAfterCommitActions afterCommit) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : ICommand<TResponse>
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var response = await unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            afterCommit.Clear(); // the operation may be retried after a transient failure

            var result = await next();

            // Handlers of one event may raise further events, so drain until none are left.
            for (var events = unitOfWork.TakeDomainEvents(); events.Count > 0; events = unitOfWork.TakeDomainEvents())
            {
                foreach (var domainEvent in events)
                {
                    await DomainEventPublisher.PublishAsync(publisher, domainEvent, token);
                }
            }

            await unitOfWork.SaveChangesAsync(token);
            return result;
        }, ct);

        await afterCommit.RunAsync(ct);
        return response;
    }
}
