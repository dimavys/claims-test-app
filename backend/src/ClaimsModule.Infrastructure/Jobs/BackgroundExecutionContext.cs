namespace ClaimsModule.Infrastructure.Jobs;

/// <summary>
/// Tenant (and system-actor) context for code running outside an HTTP request. Jobs set the organisation first so
/// the global query filters and audit stamping behave exactly as they do for a request.
/// </summary>
public sealed class BackgroundExecutionContext
{
    public Guid? OrganisationId { get; set; }
}
