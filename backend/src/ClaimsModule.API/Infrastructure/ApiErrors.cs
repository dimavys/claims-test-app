using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaimsModule.API.Infrastructure;

/// <summary>Consistent error body for every failure (FRS §10.4), including 401/403 produced by the auth middleware.</summary>
public sealed record ApiError
{
    public string Type { get; init; } = "ServerError";
    public string Title { get; init; } = string.Empty;
    public int Status { get; init; }
    public string? Detail { get; init; }
    public IDictionary<string, string[]>? Errors { get; init; }
    public Guid? CorrelationId { get; init; }

    [JsonExtensionData]
    public IDictionary<string, object?>? Extra { get; init; }
}

public static class ApiErrors
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Task WriteAsync(HttpContext context, ApiError error)
    {
        var withCorrelation = error with { CorrelationId = error.CorrelationId ?? CorrelationMiddleware.Get(context) };
        context.Response.StatusCode = withCorrelation.Status;
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(withCorrelation, Json));
    }

    public static ApiError Unauthorized() => new() { Type = "Unauthorized", Title = "Authentication is required.", Status = StatusCodes.Status401Unauthorized };
    public static ApiError Forbidden() => new() { Type = "Forbidden", Title = "Your role does not permit this action.", Status = StatusCodes.Status403Forbidden };
}
