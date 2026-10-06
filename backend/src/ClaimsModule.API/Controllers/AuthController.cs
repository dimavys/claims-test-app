using ClaimsModule.API.Contracts;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Common;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Infrastructure.Auth;
using ClaimsModule.Infrastructure.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsModule.API.Controllers;

public sealed record UserInfo(Guid Id, string UserName, string DisplayName, string Role);

public sealed record LoginResponse(string AccessToken, DateTimeOffset ExpiresAt, UserInfo User);

/// <summary>Mock identity provider: issues real JWTs for the configured hard-coded users.</summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    MockUserStore users, JwtTokenService tokens, IConfiguration configuration, ICurrentUserService current) : ControllerBase
{
    private static UserInfo ToInfo(MockUser u) => new(u.Id, u.UserName, u.DisplayName, RoleCodes.ToCode(u.Role));

    private bool RoleSwitcherEnabled => configuration.GetValue("Auth:EnableRoleSwitcher", false);

    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorShape>(StatusCodes.Status401Unauthorized)]
    public IActionResult Login(LoginRequest request)
    {
        var user = users.Validate(request.UserName, request.Password);
        if (user is null)
        {
            throw new UnauthenticatedException("Invalid user name or password.");
        }

        var token = tokens.Issue(user);
        return Ok(new LoginResponse(token.AccessToken, token.ExpiresAt, ToInfo(user)));
    }

    /// <summary>Users available to the UI's role switcher (testing only; disable with Auth:EnableRoleSwitcher=false).</summary>
    [AllowAnonymous]
    [HttpGet("users")]
    [ProducesResponseType<IReadOnlyList<UserInfo>>(StatusCodes.Status200OK)]
    public IActionResult Users() =>
        RoleSwitcherEnabled ? Ok(users.Users.Select(ToInfo).ToList()) : NotFound();

    /// <summary>Password-less token for the role switcher (testing only).</summary>
    [AllowAnonymous]
    [HttpPost("dev-token")]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    public IActionResult DevToken(DevTokenRequest request)
    {
        if (!RoleSwitcherEnabled)
        {
            return NotFound();
        }

        var user = users.Users.FirstOrDefault(u => string.Equals(u.UserName, request.UserName, StringComparison.OrdinalIgnoreCase));
        if (user is null)
        {
            throw new UnauthenticatedException("Unknown user.");
        }

        var token = tokens.Issue(user);
        return Ok(new LoginResponse(token.AccessToken, token.ExpiresAt, ToInfo(user)));
    }

    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<UserInfo>(StatusCodes.Status200OK)]
    public IActionResult Me()
    {
        var (id, _) = current.Require();
        return users.Find(id) is { } user ? Ok(ToInfo(user)) : Unauthorized();
    }
}

/// <summary>Shape of error bodies, for Swagger only.</summary>
public sealed record ApiErrorShape(string Type, string Title, int Status, IDictionary<string, string[]>? Errors, Guid? CorrelationId);
