using ClaimsModule.Application.Features.Claims.Dtos;
using ClaimsModule.Application.Features.Reference;
using ClaimsModule.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsModule.API.Controllers;

[ApiController]
[Produces("application/json")]
public sealed class ReferenceController(ISender sender) : ControllerBase
{
    /// <summary>Active cause-of-loss codes, optionally filtered by peril category.</summary>
    [HttpGet("api/reference/cause-of-loss-codes")]
    [ProducesResponseType<IReadOnlyList<CauseOfLossCodeDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CauseOfLossCodes([FromQuery] PerilCategory? perilCategory, CancellationToken ct) =>
        Ok(await sender.Send(new ListCauseOfLossCodesQuery(perilCategory), ct));

    /// <summary>Every claim status with its valid next statuses.</summary>
    [HttpGet("api/reference/claim-statuses")]
    [ProducesResponseType<IReadOnlyList<ClaimStatusInfoDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClaimStatuses(CancellationToken ct) => Ok(await sender.Send(new ListClaimStatusesQuery(), ct));

    /// <summary>Search simulated policies by policy number or client name.</summary>
    [HttpGet("api/policies/search")]
    [ProducesResponseType<IReadOnlyList<PolicyDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SearchPolicies([FromQuery] string? q, [FromQuery] int take = 20, CancellationToken ct = default) =>
        Ok(await sender.Send(new SearchPoliciesQuery(q, take), ct));

    /// <summary>Coverage types of a policy (shown during FNOL).</summary>
    [HttpGet("api/policies/{id:guid}/coverage")]
    [ProducesResponseType<PolicyCoverageDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Coverage(Guid id, CancellationToken ct) => Ok(await sender.Send(new GetPolicyCoverageQuery(id), ct));
}
