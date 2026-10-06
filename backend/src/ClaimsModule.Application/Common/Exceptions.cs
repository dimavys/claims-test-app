namespace ClaimsModule.Application.Common;

/// <summary>Request validation failed (HTTP 422). Errors are grouped by property name.</summary>
public sealed class RequestValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public RequestValidationException(IDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.") => Errors = new Dictionary<string, string[]>(errors);

    public RequestValidationException(string property, string message)
        : this(new Dictionary<string, string[]> { [property] = new[] { message } }) { }
}

public sealed class NotFoundException(string entity, object key)
    : Exception($"{entity} '{key}' was not found.");

/// <summary>No authenticated user in the current context (HTTP 401).</summary>
public sealed class UnauthenticatedException(string message) : Exception(message)
{
    public UnauthenticatedException() : this("Authentication is required.") { }
}
