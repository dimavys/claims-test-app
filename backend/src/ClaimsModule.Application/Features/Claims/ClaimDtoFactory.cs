using AutoMapper;
using ClaimsModule.Application.Features.Claims.Dtos;
using ClaimsModule.Domain.Entities;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Domain.Rules;

namespace ClaimsModule.Application.Features.Claims;

/// <summary>Shared projection helpers for the claim aggregate (kept out of the handlers so reads and writes agree).</summary>
internal static class ClaimDtoFactory
{
    public static ReserveSummaryDto BuildReserveSummary(Claim claim, IMapper mapper)
    {
        var components = claim.ReserveComponents.OrderBy(c => c.Component).Select(mapper.Map<ReserveComponentDto>).ToList();
        return new ReserveSummaryDto(components, claim.TotalReserves, components.Sum(c => c.PendingAmount), claim.ManagerOverride);
    }

    public static IReadOnlyList<ReserveTransactionDto> BuildTransactions(Claim claim, IMapper mapper) =>
        claim.ReserveComponents
            .SelectMany(c => c.History.Select(h => ToTransactionDto(c, h, mapper)))
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.ChangeSequence)
            .ToList();

    public static ReserveTransactionDto ToTransactionDto(ClaimReserveComponent component, ReserveHistory txn, IMapper mapper)
    {
        var dto = mapper.Map<ReserveTransactionDto>(txn);
        dto.Component = component.Component;
        return dto;
    }

    public static ReserveTransactionDto ToTransactionDto(Claim claim, ReserveHistory txn, IMapper mapper) =>
        ToTransactionDto(claim.ReserveComponents.Single(c => c.Id == txn.ReserveComponentId), txn, mapper);

    public static IReadOnlyList<NextStatusDto> NextStatuses(ClaimStatus from) =>
        ClaimStateMachine.Rules
            .Where(r => r.From == from)
            .Select(r => new NextStatusDto(r.To, r.MinimumRole, r.Conditions))
            .ToList();

    public static ReserveSubmissionDto ToSubmissionDto(Claim claim, ReserveSubmissionResult result, IMapper mapper)
    {
        var level = ReserveAuthorityPolicy.RequiredLevel(result.Transaction.Amount);
        return new ReserveSubmissionDto(
            ToTransactionDto(claim, result.Transaction, mapper),
            result.Warnings,
            ReserveAuthorityPolicy.Describe(level),
            result.Transaction.IsPending);
    }
}
