using AutoMapper;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Common;
using ClaimsModule.Application.Features.Claims.Commands;
using ClaimsModule.Application.Features.Claims.Dtos;
using ClaimsModule.Domain.Entities;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Domain.Rules;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Features.Claims.Queries;

// ------------------------------------------------------------------ list

public sealed record ListClaimsQuery(
    IReadOnlyList<ClaimStatus>? Statuses = null,
    DateTimeOffset? LossDateFrom = null,
    DateTimeOffset? LossDateTo = null,
    Guid? AssignedHandlerId = null,
    string? AssignedHandler = null,
    string? CauseOfLossCode = null,
    Guid? PolicyId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25) : IQuery<PagedResult<ClaimSummaryDto>>;

public sealed class ListClaimsValidator : AbstractValidator<ListClaimsQuery>
{
    public ListClaimsValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
        RuleFor(q => q).Must(q => q.LossDateFrom is null || q.LossDateTo is null || q.LossDateFrom <= q.LossDateTo)
            .WithMessage("'From' date must not be after 'To' date.").WithName(nameof(ListClaimsQuery.LossDateFrom));
    }
}

public sealed class ListClaimsHandler(
    IClaimRepository claims, IReferenceDataRepository reference, IUserDirectory users, IMapper mapper)
    : IRequestHandler<ListClaimsQuery, PagedResult<ClaimSummaryDto>>
{
    public async Task<PagedResult<ClaimSummaryDto>> Handle(ListClaimsQuery q, CancellationToken ct)
    {
        // The handler filter is a text search over user names; an explicit id narrows further.
        List<Guid>? handlerIds = null;
        if (q.AssignedHandlerId is { } id)
        {
            handlerIds = new List<Guid> { id };
        }
        else if (!string.IsNullOrWhiteSpace(q.AssignedHandler))
        {
            handlerIds = users.FindByName(q.AssignedHandler.Trim()).ToList();
            if (handlerIds.Count == 0)
            {
                return new PagedResult<ClaimSummaryDto>(Array.Empty<ClaimSummaryDto>(), 0, q.Page, q.PageSize);
            }
        }

        var filter = new ClaimListFilter(
            q.Statuses, q.LossDateFrom, q.LossDateTo, handlerIds, q.CauseOfLossCode, q.PolicyId, q.Search?.Trim(), q.Page, q.PageSize);

        var (items, total) = await claims.ListAsync(filter, ct);
        var causes = (await reference.GetCauseOfLossCodesAsync(null, activeOnly: false, ct)).ToDictionary(c => c.Code, c => c.Name);

        var rows = items.Select(c =>
        {
            var dto = mapper.Map<ClaimSummaryDto>(c);
            return dto with { CauseOfLossName = causes.GetValueOrDefault(dto.CauseOfLossCode) };
        }).ToList();

        return new PagedResult<ClaimSummaryDto>(rows, total, q.Page, q.PageSize);
    }
}

// ------------------------------------------------------------------ detail

public sealed record GetClaimDetailQuery(Guid ClaimId) : IQuery<ClaimDetailDto>;

public sealed class GetClaimDetailHandler(
    IClaimRepository claims, IReferenceDataRepository reference, IAuditLogRepository audit, IMapper mapper)
    : IRequestHandler<GetClaimDetailQuery, ClaimDetailDto>
{
    public const int RecentAuditCount = 10;

    public async Task<ClaimDetailDto> Handle(GetClaimDetailQuery q, CancellationToken ct)
    {
        var claim = await claims.GetByIdAsync(q.ClaimId, ClaimIncludes.All, track: false, ct)
            ?? throw new NotFoundException(nameof(Claim), q.ClaimId);

        var causes = (await reference.GetCauseOfLossCodesAsync(null, activeOnly: false, ct)).ToDictionary(c => c.Code, c => c.Name);
        var (recent, _) = await audit.ListAsync(claim.Id, 1, RecentAuditCount, ct);

        var dto = mapper.Map<ClaimDetailDto>(claim);
        dto.LossEvent = dto.LossEvent with { CauseOfLossName = causes.GetValueOrDefault(dto.LossEvent.CauseOfLossCode) };
        dto.ReserveSummary = ClaimDtoFactory.BuildReserveSummary(claim, mapper);
        dto.RecentAudit = recent.Select(mapper.Map<AuditEntryDto>).ToList();
        dto.ValidNextStatuses = ClaimDtoFactory.NextStatuses(claim.Status);
        return dto;
    }
}

// ------------------------------------------------------------------ audit

public sealed record GetClaimAuditQuery(Guid ClaimId, int Page = 1, int PageSize = 25) : IQuery<PagedResult<AuditEntryDto>>;

public sealed class GetClaimAuditValidator : AbstractValidator<GetClaimAuditQuery>
{
    public GetClaimAuditValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetClaimAuditHandler(IClaimRepository claims, IAuditLogRepository audit, IMapper mapper)
    : IRequestHandler<GetClaimAuditQuery, PagedResult<AuditEntryDto>>
{
    public async Task<PagedResult<AuditEntryDto>> Handle(GetClaimAuditQuery q, CancellationToken ct)
    {
        if (!await claims.ExistsAsync(q.ClaimId, ct))
        {
            throw new NotFoundException(nameof(Claim), q.ClaimId);
        }

        var (items, total) = await audit.ListAsync(q.ClaimId, q.Page, q.PageSize, ct);
        return new PagedResult<AuditEntryDto>(items.Select(mapper.Map<AuditEntryDto>).ToList(), total, q.Page, q.PageSize);
    }
}

// ------------------------------------------------------------------ documents

public sealed record ListClaimDocumentsQuery(Guid ClaimId) : IQuery<IReadOnlyList<DocumentDto>>;

public sealed class ListClaimDocumentsHandler(IClaimRepository claims, IStorageService storage, TimeProvider time, IMapper mapper)
    : IRequestHandler<ListClaimDocumentsQuery, IReadOnlyList<DocumentDto>>
{
    /// <summary>FRS BR-D-02: download links are short-lived (1 hour).</summary>
    public static readonly TimeSpan UrlTimeToLive = TimeSpan.FromHours(1);

    public async Task<IReadOnlyList<DocumentDto>> Handle(ListClaimDocumentsQuery q, CancellationToken ct)
    {
        if (!await claims.ExistsAsync(q.ClaimId, ct))
        {
            throw new NotFoundException(nameof(Claim), q.ClaimId);
        }

        var documents = await claims.GetDocumentsAsync(q.ClaimId, ct);
        var expiresAt = time.GetUtcNow().Add(UrlTimeToLive);
        var result = new List<DocumentDto>(documents.Count);
        foreach (var document in documents.OrderByDescending(d => d.UploadedAt))
        {
            var dto = mapper.Map<DocumentDto>(document);
            dto.DownloadUrl = (await storage.GetDownloadUrlAsync(document.BlobPath, UrlTimeToLive, ct)).ToString();
            dto.DownloadUrlExpiresAt = expiresAt;
            result.Add(dto);
        }

        return result;
    }
}

// ------------------------------------------------------------------ closure pre-flight

public sealed record GetClosurePreflightQuery(Guid ClaimId, string? ClosureJustification = null) : IQuery<ClosurePreflightDto>;

public sealed class GetClosurePreflightHandler(IClaimRepository claims) : IRequestHandler<GetClosurePreflightQuery, ClosurePreflightDto>
{
    public async Task<ClosurePreflightDto> Handle(GetClosurePreflightQuery q, CancellationToken ct)
    {
        var claim = await claims.GetByIdAsync(q.ClaimId, ClaimIncludes.ForUpdate, track: false, ct)
            ?? throw new NotFoundException(nameof(Claim), q.ClaimId);

        var blockers = new List<string>();
        if (!ClaimStateMachine.IsAllowed(claim.Status, ClaimStatus.Closed))
        {
            blockers.Add($"Transition from {claim.Status} to Closed is not permitted.");
        }

        blockers.AddRange(claim.ClosureBlockers(q.ClosureJustification));

        var openBalance = claim.ReserveComponents.Where(c => c.CurrentAmount > 0).Sum(c => c.CurrentAmount);
        return new ClosurePreflightDto(
            blockers.Count == 0, blockers, openBalance, openBalance > 0 && string.IsNullOrWhiteSpace(q.ClosureJustification));
    }
}

// ------------------------------------------------------------------ explicit validation (FRS §5.4)

/// <summary>Dry-run of FNOL validation: reports what Create would reject (Critical) or record as warnings, saving nothing.</summary>
public sealed record ValidateClaimQuery(CreateClaimCommand Claim) : IQuery<ValidationReportDto>;

public sealed class ValidateClaimHandler(IValidator<CreateClaimCommand> validator, IReferenceDataRepository reference)
    : IRequestHandler<ValidateClaimQuery, ValidationReportDto>
{
    public async Task<ValidationReportDto> Handle(ValidateClaimQuery q, CancellationToken ct)
    {
        var cmd = q.Claim;
        var critical = (await validator.ValidateAsync(cmd, ct)).Errors.Select(e => e.ErrorMessage).Distinct().ToList();
        var warnings = new List<string>();

        if (!(cmd.Parties ?? Array.Empty<PartyInput>()).Any(p => p.PartyRole == PartyRole.Claimant))
        {
            critical.Add(ValidationMessages.NoClaimant);
        }

        if (cmd.PolicyId is null)
        {
            warnings.Add(ValidationMessages.NoPolicy);
        }
        else if (await reference.GetPolicyAsync(cmd.PolicyId.Value, ct) is { } policy)
        {
            if (!policy.Covers(cmd.LossDate))
            {
                warnings.Add(ValidationMessages.LossDateOutsidePolicy);
            }
        }
        else
        {
            critical.Add("The selected policy was not found.");
        }

        if (!(cmd.RiskObjects ?? Array.Empty<RiskObjectInput>()).Any())
        {
            warnings.Add(ValidationMessages.NoRiskObjects);
        }

        return new ValidationReportDto(critical, warnings);
    }
}
