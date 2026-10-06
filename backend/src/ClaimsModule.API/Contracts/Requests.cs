using ClaimsModule.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsModule.API.Contracts;

public sealed record LoginRequest(string UserName, string Password);

public sealed record DevTokenRequest(string UserName);

public sealed record TransitionStatusRequest(
    ClaimStatus TargetStatus,
    string? Reason = null,
    bool AcknowledgeWarnings = false,
    string? ClosureJustification = null);

public sealed record UpdateNotesRequest(string? Notes);

public sealed record ManagerOverrideRequest(bool Value = true);

public sealed record SubmitReserveRequest(
    ReserveComponentType Component,
    decimal Amount,
    string ChangeReason,
    ReserveTransactionType TransactionType = ReserveTransactionType.Add);

public sealed record AdjustReserveRequest(decimal Amount, string ChangeReason);

public sealed record RejectReserveRequest(string RejectionReason);

/// <summary>GET /api/claims query string (FRS §10.1).</summary>
public sealed class ListClaimsRequest
{
    [FromQuery(Name = "status")] public ClaimStatus[]? Status { get; init; }
    [FromQuery(Name = "dateFrom")] public DateTimeOffset? DateFrom { get; init; }
    [FromQuery(Name = "dateTo")] public DateTimeOffset? DateTo { get; init; }
    [FromQuery(Name = "assignedHandlerId")] public Guid? AssignedHandlerId { get; init; }
    [FromQuery(Name = "assignedHandler")] public string? AssignedHandler { get; init; }
    [FromQuery(Name = "causeOfLossCode")] public string? CauseOfLossCode { get; init; }
    [FromQuery(Name = "policyId")] public Guid? PolicyId { get; init; }
    [FromQuery(Name = "search")] public string? Search { get; init; }
    [FromQuery(Name = "page")] public int Page { get; init; } = 1;
    [FromQuery(Name = "pageSize")] public int PageSize { get; init; } = 25;
}
