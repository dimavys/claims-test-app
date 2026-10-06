using ClaimsModule.API.Contracts;
using ClaimsModule.Application.Common;
using ClaimsModule.Application.Features.Claims.Commands;
using ClaimsModule.Application.Features.Claims.Dtos;
using ClaimsModule.Application.Features.Claims.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace ClaimsModule.API.Controllers;

[ApiController]
[Route("api/claims")]
[Produces("application/json")]
[ProducesResponseType<ApiErrorShape>(StatusCodes.Status401Unauthorized)]
public sealed class ClaimsController(ISender sender) : ControllerBase
{
    private const long MaxUploadBytes = DocumentRules.MaxFileSizeBytes + 1024 * 1024; // file + multipart overhead

    /// <summary>FNOL: creates the claim, loss event, parties, risk objects and optional initial reserve in one transaction.</summary>
    [HttpPost]
    [ProducesResponseType<ClaimCreatedDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorShape>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(CreateClaimCommand command, CancellationToken ct)
    {
        var result = await sender.Send(command, ct);
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    /// <summary>Dry-run of intake validation: Critical issues block, warnings do not. Nothing is saved.</summary>
    [HttpPost("validate")]
    [ProducesResponseType<ValidationReportDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Validate(CreateClaimCommand command, CancellationToken ct) =>
        Ok(await sender.Send(new ValidateClaimQuery(command), ct));

    [HttpGet]
    [ProducesResponseType<PagedResult<ClaimSummaryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ListClaimsRequest r, CancellationToken ct) =>
        Ok(await sender.Send(new ListClaimsQuery(
            r.Status, r.DateFrom, r.DateTo, r.AssignedHandlerId, r.AssignedHandler, r.CauseOfLossCode, r.PolicyId, r.Search, r.Page, r.PageSize), ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ClaimDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorShape>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Ok(await sender.Send(new GetClaimDetailQuery(id), ct));

    /// <summary>Transitions the claim; returns 422 with the valid next statuses or blocking conditions when refused.</summary>
    [HttpPut("{id:guid}/status")]
    [ProducesResponseType<ClaimStatusChangedDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiErrorShape>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Transition(Guid id, TransitionStatusRequest body, CancellationToken ct) =>
        Ok(await sender.Send(new TransitionClaimStatusCommand(
            id, body.TargetStatus, body.Reason, body.AcknowledgeWarnings, body.ClosureJustification), ct));

    /// <summary>What closing would require, without changing anything (backs the UI's pre-flight checklist).</summary>
    [HttpGet("{id:guid}/closure-preflight")]
    [ProducesResponseType<ClosurePreflightDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ClosurePreflight(Guid id, [FromQuery] string? closureJustification, CancellationToken ct) =>
        Ok(await sender.Send(new GetClosurePreflightQuery(id, closureJustification), ct));

    [HttpGet("{id:guid}/audit")]
    [ProducesResponseType<PagedResult<AuditEntryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Audit(Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Ok(await sender.Send(new GetClaimAuditQuery(id, page, pageSize), ct));

    [HttpPost("{id:guid}/parties")]
    [ProducesResponseType<ClaimPartyDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddParty(Guid id, PartyInput party, CancellationToken ct)
    {
        var result = await sender.Send(new AddClaimPartyCommand(id, party), ct);
        return Created($"/api/claims/{id}", result);
    }

    /// <summary>Soft-removes a party; 422 if it is the last Claimant.</summary>
    [HttpDelete("{id:guid}/parties/{partyId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorShape>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RemoveParty(Guid id, Guid partyId, CancellationToken ct)
    {
        await sender.Send(new RemoveClaimPartyCommand(id, partyId), ct);
        return NoContent();
    }

    [HttpPut("{id:guid}/notes")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateNotes(Guid id, UpdateNotesRequest body, CancellationToken ct)
    {
        await sender.Send(new UpdateClaimNotesCommand(id, body.Notes), ct);
        return NoContent();
    }

    /// <summary>Manager-only: allow total reserves above $10,000,000 on this claim.</summary>
    [HttpPut("{id:guid}/manager-override")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiErrorShape>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ManagerOverride(Guid id, ManagerOverrideRequest body, CancellationToken ct)
    {
        await sender.Send(new SetManagerOverrideCommand(id, body.Value), ct);
        return NoContent();
    }

    /// <summary>Uploads a document (PDF, JPEG, PNG, DOCX, XLSX, TXT, CSV; max 50 MB) to blob storage.</summary>
    [HttpPost("{id:guid}/documents")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxUploadBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxUploadBytes)]
    [ProducesResponseType<DocumentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiErrorShape>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UploadDocument(
        Guid id, IFormFile? file, [FromForm] string? documentType, [FromForm] string? notes, CancellationToken ct)
    {
        if (file is null)
        {
            throw new RequestValidationException("File", "A file is required.");
        }

        await using var stream = file.OpenReadStream();
        var result = await sender.Send(
            new UploadClaimDocumentCommand(id, file.FileName, file.ContentType, file.Length, stream, documentType, notes), ct);
        return Created($"/api/claims/{id}/documents", result);
    }

    /// <summary>Documents with short-lived (1 hour) download URLs.</summary>
    [HttpGet("{id:guid}/documents")]
    [ProducesResponseType<IReadOnlyList<DocumentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Documents(Guid id, CancellationToken ct) => Ok(await sender.Send(new ListClaimDocumentsQuery(id), ct));
}
