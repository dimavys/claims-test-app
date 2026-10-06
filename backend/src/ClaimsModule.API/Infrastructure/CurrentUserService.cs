using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Common;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Infrastructure.Auth;
using ClaimsModule.Infrastructure.Jobs;
using ClaimsModule.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace ClaimsModule.API.Infrastructure;

/// <summary>
/// The caller from the validated JWT. Outside a request (Hangfire jobs) there is no user: the tenant comes from the
/// job's <see cref="BackgroundExecutionContext"/> and the actor is the system (null user id).
/// </summary>
public sealed class CurrentUserService(
    IHttpContextAccessor accessor, IOptions<TenantOptions> tenant, BackgroundExecutionContext background) : ICurrentUserService
{
    private System.Security.Claims.ClaimsPrincipal? Principal =>
        accessor.HttpContext?.User is { Identity.IsAuthenticated: true } p ? p : null;

    public Guid? UserId => Guid.TryParse(Principal?.FindFirst("sub")?.Value, out var id) ? id : null;

    public string? UserName => Principal?.FindFirst(JwtTokenService.NameClaim)?.Value;

    public UserRole? Role => RoleCodes.TryParse(Principal?.FindFirst(JwtTokenService.RoleClaim)?.Value, out var role) ? role : null;

    public Guid OrganisationId =>
        Guid.TryParse(Principal?.FindFirst(JwtTokenService.OrganisationClaim)?.Value, out var org) ? org
        : background.OrganisationId ?? tenant.Value.OrganisationId;
}
