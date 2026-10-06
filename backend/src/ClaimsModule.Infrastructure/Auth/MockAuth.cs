using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Common;
using ClaimsModule.Infrastructure.Options;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ClaimsModule.Infrastructure.Auth;

/// <summary>Hard-coded users (from configuration) standing in for an identity provider. Doubles as the user directory.</summary>
public sealed class MockUserStore : IUserDirectory
{
    private readonly IReadOnlyList<MockUser> _users;

    public MockUserStore(IOptions<MockUsersOptions> options) => _users = options.Value.Users;

    public IReadOnlyList<MockUser> Users => _users;

    public MockUser? Validate(string? userName, string? password) =>
        _users.FirstOrDefault(u =>
            string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(u.Password, password, StringComparison.Ordinal));

    public MockUser? Find(Guid id) => _users.FirstOrDefault(u => u.Id == id);

    public string? GetDisplayName(Guid? userId) => userId is { } id ? Find(id)?.DisplayName : null;

    public IReadOnlyList<Guid> FindByName(string text) =>
        _users.Where(u => u.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase) ||
                          u.UserName.Contains(text, StringComparison.OrdinalIgnoreCase))
            .Select(u => u.Id).ToList();
}

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);

public sealed class JwtTokenService(IOptions<JwtOptions> jwt, IOptions<TenantOptions> tenant, TimeProvider time)
{
    public const string OrganisationClaim = "org";
    public const string RoleClaim = "role";
    public const string NameClaim = "name";

    public static SymmetricSecurityKey KeyFor(JwtOptions options) => new(Encoding.UTF8.GetBytes(options.SigningKey));

    public IssuedToken Issue(MockUser user)
    {
        var options = jwt.Value;
        var now = time.GetUtcNow();
        var expires = now.AddMinutes(options.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(NameClaim, user.DisplayName),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new Claim(RoleClaim, RoleCodes.ToCode(user.Role)),
            new Claim(OrganisationClaim, tenant.Value.OrganisationId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        var token = new JwtSecurityToken(
            options.Issuer, options.Audience, claims, now.UtcDateTime, expires.UtcDateTime,
            new SigningCredentials(KeyFor(options), SecurityAlgorithms.HmacSha256));

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
