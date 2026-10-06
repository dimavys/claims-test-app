using ClaimsModule.Domain.Enums;

namespace ClaimsModule.Application.Abstractions;

/// <summary>Identity and tenant of the caller (HTTP request) or of the system actor (background jobs).</summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? UserName { get; }
    UserRole? Role { get; }

    /// <summary>Tenant used by the global query filters and stamped on new rows.</summary>
    Guid OrganisationId { get; }
}
