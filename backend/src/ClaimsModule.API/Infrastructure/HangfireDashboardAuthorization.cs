using Hangfire.Dashboard;

namespace ClaimsModule.API.Infrastructure;

/// <summary>The dashboard has no JWT support, so it is opened explicitly via configuration (on for local dev and the demo).</summary>
public sealed class HangfireDashboardAuthorization(bool allowAnonymous) : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context) => allowAnonymous;
}
