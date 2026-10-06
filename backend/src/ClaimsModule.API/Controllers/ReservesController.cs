using ClaimsModule.API.Contracts;
using ClaimsModule.Application.Common;
using ClaimsModule.Application.Features.Claims.Dtos;
using ClaimsModule.Application.Features.Reserves;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsModule.API.Controllers;

[ApiController]
[Route("api/claims/{claimId:guid}/reserves")]
[Produces("application/json")]
public sealed class ReservesController(ISender sender) : ControllerBase
{
    /// <summary>Opens or changes a reserve. ≤ $10k is auto-approved (GL job queued); larger amounts wait for approval.</summary>
    [HttpPost]
    [ProducesResponseType<ReserveSubmissionDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorShape>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Submit(Guid claimId, SubmitReserveRequest body, CancellationToken ct)
    {
        var result = await sender.Send(new SubmitReserveCommand(claimId, body.Component, body.Amount, body.ChangeReason, body.TransactionType), ct);
        return Created($"/api/claims/{claimId}/reserves", result);
    }

    /// <summary>Adjusts an existing reserve component (reserveId is the component id).</summary>
    [HttpPut("{reserveId:guid}")]
    [ProducesResponseType<ReserveSubmissionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Adjust(Guid claimId, Guid reserveId, AdjustReserveRequest body, CancellationToken ct) =>
        Ok(await sender.Send(new AdjustReserveCommand(claimId, reserveId, body.Amount, body.ChangeReason), ct));

    /// <summary>Current balance per component plus the full transaction history.</summary>
    [HttpGet]
    [ProducesResponseType<ReservesDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid claimId, CancellationToken ct) => Ok(await sender.Send(new GetReservesQuery(claimId), ct));

    /// <summary>Supervisor or Manager only. The amount-level authority and the self-approval ban are enforced by the domain.</summary>
    [HttpPost("{txnId:guid}/approve")]
    [Authorize(Roles = RoleCodes.Approvers)]
    [ProducesResponseType<ReserveTransactionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorShape>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiErrorShape>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Approve(Guid claimId, Guid txnId, CancellationToken ct) =>
        Ok(await sender.Send(new ApproveReserveCommand(claimId, txnId), ct));

    [HttpPost("{txnId:guid}/reject")]
    [Authorize(Roles = RoleCodes.Approvers)]
    [ProducesResponseType<ReserveTransactionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reject(Guid claimId, Guid txnId, RejectReserveRequest body, CancellationToken ct) =>
        Ok(await sender.Send(new RejectReserveCommand(claimId, txnId, body.RejectionReason), ct));

    /// <summary>The submitter withdraws their own pending reserve (status → Cancelled).</summary>
    [HttpPost("{txnId:guid}/retract")]
    [ProducesResponseType<ReserveTransactionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Retract(Guid claimId, Guid txnId, CancellationToken ct) =>
        Ok(await sender.Send(new RetractReserveCommand(claimId, txnId), ct));

    /// <summary>Re-queues a GL posting that failed after all retries.</summary>
    [HttpPost("{txnId:guid}/retry-posting")]
    [ProducesResponseType<ReserveTransactionDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> RetryPosting(Guid claimId, Guid txnId, CancellationToken ct) =>
        Ok(await sender.Send(new RetryGlPostingCommand(claimId, txnId), ct));
}
