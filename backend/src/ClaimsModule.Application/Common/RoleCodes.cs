using ClaimsModule.Domain.Enums;

namespace ClaimsModule.Application.Common;

/// <summary>Role codes used in tokens and [Authorize] attributes (FRS §3): handler, supervisor, manager.</summary>
public static class RoleCodes
{
    public const string Handler = "handler";
    public const string Supervisor = "supervisor";
    public const string Manager = "manager";

    /// <summary>Roles allowed to approve or reject reserves (amount-level authority is enforced in the domain).</summary>
    public const string Approvers = Supervisor + "," + Manager;

    public static string ToCode(UserRole role) => role switch
    {
        UserRole.Handler => Handler,
        UserRole.Supervisor => Supervisor,
        UserRole.Manager => Manager,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    public static bool TryParse(string? code, out UserRole role)
    {
        switch (code?.Trim().ToLowerInvariant())
        {
            case Handler: role = UserRole.Handler; return true;
            case Supervisor: role = UserRole.Supervisor; return true;
            case Manager: role = UserRole.Manager; return true;
            default: role = default; return false;
        }
    }
}
