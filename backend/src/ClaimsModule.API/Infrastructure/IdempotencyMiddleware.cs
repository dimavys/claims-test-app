using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace ClaimsModule.API.Infrastructure;

/// <summary>
/// Honours the Idempotency-Key header on write requests (FRS §10): the first successful response is stored and replayed
/// for any repeat of the same key, so a client retry cannot create a second claim or reserve. Concurrent duplicates wait
/// for the first to finish. Failures are not stored, so a corrected request can reuse its key.
/// Storage is per-instance memory; a multi-instance deployment would swap in a shared cache or table.
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next, IMemoryCache cache)
{
    public const string HeaderName = "Idempotency-Key";
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    private sealed record StoredResponse(string Fingerprint, int Status, string? ContentType, string? Location, byte[] Body);

    public async Task Invoke(HttpContext context)
    {
        var request = context.Request;
        if (!HttpMethods.IsPost(request.Method) && !HttpMethods.IsPut(request.Method) && !HttpMethods.IsDelete(request.Method)
            || string.IsNullOrWhiteSpace(request.Headers[HeaderName]))
        {
            await next(context);
            return;
        }

        var key = request.Headers[HeaderName].ToString().Trim();
        if (key.Length > 128)
        {
            await ApiErrors.WriteAsync(context, new ApiError
            {
                Type = "ValidationError", Title = "Idempotency-Key must be at most 128 characters.", Status = StatusCodes.Status422UnprocessableEntity,
            });
            return;
        }

        var user = context.User.FindFirst("sub")?.Value ?? "anonymous";
        var cacheKey = $"idem:{user}:{key}";
        var fingerprint = await FingerprintAsync(request);

        var gate = Gates.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(context.RequestAborted);
        try
        {
            if (cache.TryGetValue(cacheKey, out StoredResponse? stored) && stored is not null)
            {
                if (stored.Fingerprint != fingerprint)
                {
                    await ApiErrors.WriteAsync(context, new ApiError
                    {
                        Type = "ValidationError", Status = StatusCodes.Status422UnprocessableEntity,
                        Title = "This Idempotency-Key was already used with a different request.",
                    });
                    return;
                }

                await ReplayAsync(context, stored);
                return;
            }

            var original = context.Response.Body;
            await using var buffer = new MemoryStream();
            context.Response.Body = buffer;
            try
            {
                await next(context);
            }
            finally
            {
                context.Response.Body = original;
            }

            buffer.Position = 0;
            if (context.Response.StatusCode is >= 200 and < 300)
            {
                cache.Set(cacheKey, new StoredResponse(
                    fingerprint, context.Response.StatusCode, context.Response.ContentType,
                    context.Response.Headers.Location.ToString() is { Length: > 0 } l ? l : null, buffer.ToArray()),
                    Retention);
            }

            await buffer.CopyToAsync(original, context.RequestAborted);
        }
        finally
        {
            gate.Release();
            Gates.TryRemove(cacheKey, out _); // waiters already hold the semaphore; later requests hit the cache first
        }
    }

    private static async Task ReplayAsync(HttpContext context, StoredResponse stored)
    {
        context.Response.StatusCode = stored.Status;
        context.Response.ContentType = stored.ContentType;
        if (stored.Location is not null)
        {
            context.Response.Headers.Location = stored.Location;
        }

        context.Response.Headers["Idempotent-Replayed"] = "true";
        await context.Response.Body.WriteAsync(stored.Body, context.RequestAborted);
    }

    /// <summary>Method + path + query, plus a hash of JSON bodies (uploads are not hashed to avoid buffering 50 MB).</summary>
    private static async Task<string> FingerprintAsync(HttpRequest request)
    {
        var basis = $"{request.Method} {request.Path}{request.QueryString}";
        if (request.ContentType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true)
        {
            request.EnableBuffering();
            using var sha = SHA256.Create();
            var hash = await sha.ComputeHashAsync(request.Body);
            request.Body.Position = 0;
            basis += "|" + Convert.ToHexString(hash);
        }

        return basis;
    }
}
