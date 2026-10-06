using ClaimsModule.Application.Common;
using ClaimsModule.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace ClaimsModule.API.Infrastructure;

/// <summary>Translates exceptions into the structured error body with the right HTTP status.</summary>
public sealed class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, IHostEnvironment env)
{
    public async Task Invoke(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex) when (!context.Response.HasStarted)
        {
            await ApiErrors.WriteAsync(context, Map(ex));
        }
    }

    private ApiError Map(Exception ex)
    {
        switch (ex)
        {
            case RequestValidationException v:
                return new ApiError
                {
                    Type = "ValidationError", Title = "One or more validation errors occurred.",
                    Status = StatusCodes.Status422UnprocessableEntity, Errors = Copy(v.Errors),
                };

            case ClaimTransitionException t:
                return new ApiError
                {
                    Type = "ValidationError", Title = t.Message, Status = StatusCodes.Status422UnprocessableEntity,
                    Errors = Copy(t.Errors),
                    Extra = new Dictionary<string, object?>
                    {
                        ["validNextStatuses"] = t.ValidNextStatuses.Select(s => s.ToString()).ToArray(),
                        ["blockingConditions"] = t.BlockingConditions,
                    },
                };

            case DomainException d:
                return d.Kind switch
                {
                    DomainErrorKind.Authority => new ApiError { Type = "Forbidden", Title = d.Message, Status = StatusCodes.Status403Forbidden },
                    DomainErrorKind.NotFound => new ApiError { Type = "NotFound", Title = d.Message, Status = StatusCodes.Status404NotFound },
                    DomainErrorKind.Conflict => new ApiError { Type = "Conflict", Title = d.Message, Status = StatusCodes.Status409Conflict },
                    _ => new ApiError
                    {
                        Type = "ValidationError", Title = "One or more validation errors occurred.",
                        Status = StatusCodes.Status422UnprocessableEntity, Errors = Copy(d.Errors),
                    },
                };

            case NotFoundException n:
                return new ApiError { Type = "NotFound", Title = n.Message, Status = StatusCodes.Status404NotFound };

            case UnauthenticatedException u:
                return ApiErrors.Unauthorized() with { Title = u.Message };

            case DbUpdateConcurrencyException:
                logger.LogWarning(ex, "Concurrency conflict");
                return new ApiError
                {
                    Type = "Conflict", Status = StatusCodes.Status409Conflict,
                    Title = "The record was changed by someone else. Reload and try again.",
                };

            case DbUpdateException when IsUniqueViolation(ex):
                logger.LogWarning(ex, "Unique constraint violation");
                return new ApiError
                {
                    Type = "Conflict", Status = StatusCodes.Status409Conflict,
                    Title = "A conflicting record already exists (possibly a concurrent request). Reload and try again.",
                };

            case BadHttpRequestException b:
                return new ApiError { Type = "BadRequest", Title = b.Message, Status = b.StatusCode };

            case System.Text.Json.JsonException j:
                return new ApiError { Type = "BadRequest", Title = "The request body is not valid JSON.", Detail = j.Message, Status = StatusCodes.Status400BadRequest };

            default:
                logger.LogError(ex, "Unhandled exception");
                return new ApiError
                {
                    Type = "ServerError", Title = "An unexpected error occurred.", Status = StatusCodes.Status500InternalServerError,
                    Detail = env.IsDevelopment() ? ex.ToString() : "Quote the correlation id when contacting support.",
                };
        }
    }

    private static Dictionary<string, string[]> Copy(IReadOnlyDictionary<string, string[]> errors) => new(errors);

    private static bool IsUniqueViolation(Exception ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
            {
                return true;
            }
        }

        return false;
    }
}
