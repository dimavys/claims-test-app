using ClaimsModule.Application.Abstractions;
using Serilog.Context;

namespace ClaimsModule.API.Infrastructure;

/// <summary>Per-request correlation id, propagated to audit entries, logs and the response header.</summary>
public sealed class CorrelationContext : ICorrelationContext
{
    public Guid CorrelationId { get; set; } = Guid.NewGuid();
}

public sealed class CorrelationMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    private const string ItemKey = "CorrelationId";

    public async Task Invoke(HttpContext context)
    {
        var correlationId = Guid.TryParse(context.Request.Headers[HeaderName], out var supplied) ? supplied : Guid.NewGuid();
        context.Items[ItemKey] = correlationId;
        context.RequestServices.GetRequiredService<CorrelationContext>().CorrelationId = correlationId;
        context.Response.Headers[HeaderName] = correlationId.ToString();

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    public static Guid? Get(HttpContext context) => context.Items.TryGetValue(ItemKey, out var id) ? (Guid?)id : null;
}
