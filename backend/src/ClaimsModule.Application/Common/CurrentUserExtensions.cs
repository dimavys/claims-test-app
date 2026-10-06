using ClaimsModule.Application.Abstractions;
using ClaimsModule.Domain.Enums;

namespace ClaimsModule.Application.Common;

public static class CurrentUserExtensions
{
    /// <summary>Returns the caller's id and role or throws when the request is anonymous.</summary>
    public static (Guid UserId, UserRole Role) Require(this ICurrentUserService user) =>
        user.UserId is { } id && user.Role is { } role ? (id, role) : throw new UnauthenticatedException();
}
