using ClaimsModule.Domain.Enums;

namespace ClaimsModule.Domain.Common;

public enum DomainErrorKind
{
    /// <summary>Business rule or validation failure (HTTP 422).</summary>
    Validation,
    /// <summary>Caller's role lacks authority for the action (HTTP 403).</summary>
    Authority,
    NotFound,
    Conflict
}

public class DomainException : Exception
{
    public DomainErrorKind Kind { get; }
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public DomainException(string message, DomainErrorKind kind = DomainErrorKind.Validation, string? field = null)
        : base(message)
    {
        Kind = kind;
        Errors = new Dictionary<string, string[]> { [field ?? "Domain"] = new[] { message } };
    }

    public DomainException(string message, IDictionary<string, string[]> errors, DomainErrorKind kind = DomainErrorKind.Validation)
        : base(message)
    {
        Kind = kind;
        Errors = new Dictionary<string, string[]>(errors);
    }

    public static DomainException Authority(string message) => new(message, DomainErrorKind.Authority);
    public static DomainException NotFound(string message) => new(message, DomainErrorKind.NotFound);
}

/// <summary>Raised when a status transition is not allowed or its conditions are not met (BR-ST-01/02/03).</summary>
public sealed class ClaimTransitionException : DomainException
{
    public IReadOnlyList<string> BlockingConditions { get; }
    public IReadOnlyList<ClaimStatus> ValidNextStatuses { get; }

    private ClaimTransitionException(string message, IReadOnlyList<string> blocking, IReadOnlyList<ClaimStatus> validNext)
        : base(message, new Dictionary<string, string[]> { ["StatusTransition"] = blocking.ToArray() })
    {
        BlockingConditions = blocking;
        ValidNextStatuses = validNext;
    }

    public static ClaimTransitionException NotPermitted(ClaimStatus from, ClaimStatus to, IReadOnlyList<ClaimStatus> validNext)
    {
        var valid = validNext.Count == 0 ? "none" : string.Join(", ", validNext);
        var message = $"Transition from {from} to {to} is not permitted.";
        return new ClaimTransitionException(message, new[] { $"{message} Valid next statuses: {valid}." }, validNext);
    }

    public static ClaimTransitionException Blocked(ClaimStatus from, ClaimStatus to, IReadOnlyList<string> blocking) =>
        new($"Claim cannot move from {from} to {to}.", blocking, ClaimStateMachineAccessor.ValidNext(from));
}

internal static class ClaimStateMachineAccessor
{
    public static IReadOnlyList<ClaimStatus> ValidNext(ClaimStatus from) => Rules.ClaimStateMachine.ValidNextStatuses(from);
}
