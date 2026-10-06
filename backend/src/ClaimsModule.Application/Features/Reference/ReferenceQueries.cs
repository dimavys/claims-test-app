using AutoMapper;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Common;
using ClaimsModule.Application.Features.Claims.Dtos;
using ClaimsModule.Domain.Entities;
using ClaimsModule.Domain.Enums;
using MediatR;

namespace ClaimsModule.Application.Features.Reference;

public sealed record SearchPoliciesQuery(string? Q = null, int Take = 20) : IQuery<IReadOnlyList<PolicyDto>>;

public sealed class SearchPoliciesHandler(IReferenceDataRepository reference, IMapper mapper)
    : IRequestHandler<SearchPoliciesQuery, IReadOnlyList<PolicyDto>>
{
    public async Task<IReadOnlyList<PolicyDto>> Handle(SearchPoliciesQuery q, CancellationToken ct)
    {
        var policies = await reference.SearchPoliciesAsync(q.Q?.Trim(), Math.Clamp(q.Take, 1, 50), ct);
        return policies.Select(mapper.Map<PolicyDto>).ToList();
    }
}

public sealed record PolicyCoverageDto(Guid PolicyId, string PolicyNumber, string ClientName, IReadOnlyList<string> CoverageTypes);

public sealed record GetPolicyCoverageQuery(Guid PolicyId) : IQuery<PolicyCoverageDto>;

public sealed class GetPolicyCoverageHandler(IReferenceDataRepository reference) : IRequestHandler<GetPolicyCoverageQuery, PolicyCoverageDto>
{
    public async Task<PolicyCoverageDto> Handle(GetPolicyCoverageQuery q, CancellationToken ct)
    {
        var policy = await reference.GetPolicyAsync(q.PolicyId, ct) ?? throw new NotFoundException(nameof(Policy), q.PolicyId);
        return new PolicyCoverageDto(policy.Id, policy.PolicyNumber, policy.ClientName, policy.CoverageTypeList());
    }
}

public sealed record ListCauseOfLossCodesQuery(PerilCategory? PerilCategory = null) : IQuery<IReadOnlyList<CauseOfLossCodeDto>>;

public sealed class ListCauseOfLossCodesHandler(IReferenceDataRepository reference)
    : IRequestHandler<ListCauseOfLossCodesQuery, IReadOnlyList<CauseOfLossCodeDto>>
{
    public async Task<IReadOnlyList<CauseOfLossCodeDto>> Handle(ListCauseOfLossCodesQuery q, CancellationToken ct) =>
        (await reference.GetCauseOfLossCodesAsync(q.PerilCategory, activeOnly: true, ct))
            .Select(c => new CauseOfLossCodeDto(c.Code, c.Name, c.PerilCategory))
            .ToList();
}

public sealed record ListClaimStatusesQuery : IQuery<IReadOnlyList<ClaimStatusInfoDto>>;

/// <summary>Statuses with their allowed next statuses, read from the seeded ClaimStatusTransitions table.</summary>
public sealed class ListClaimStatusesHandler(IReferenceDataRepository reference)
    : IRequestHandler<ListClaimStatusesQuery, IReadOnlyList<ClaimStatusInfoDto>>
{
    public async Task<IReadOnlyList<ClaimStatusInfoDto>> Handle(ListClaimStatusesQuery q, CancellationToken ct)
    {
        var transitions = await reference.GetStatusTransitionsAsync(ct);
        return Enum.GetValues<ClaimStatus>()
            .Select(status => new ClaimStatusInfoDto(
                status,
                transitions.Where(t => t.FromStatus == status)
                    .Select(t => new NextStatusDto(t.ToStatus, t.RequiredPermission, t.Description ?? string.Empty))
                    .ToList()))
            .ToList();
    }
}
