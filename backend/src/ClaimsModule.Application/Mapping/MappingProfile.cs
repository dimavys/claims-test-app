using AutoMapper;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Features.Claims.Dtos;
using ClaimsModule.Domain.Entities;

namespace ClaimsModule.Application.Mapping;

/// <summary>Resolves a user id to a display name through the user directory.</summary>
public sealed class UserNameResolver(IUserDirectory directory) : IMemberValueResolver<object, object, Guid?, string?>
{
    public string? Resolve(object source, object destination, Guid? sourceMember, string? destMember, ResolutionContext context) =>
        directory.GetDisplayName(sourceMember);
}

/// <summary>Audit entries written by jobs have no user: they are attributed to "System".</summary>
public sealed class ActorNameResolver(IUserDirectory directory) : IMemberValueResolver<object, object, Guid?, string?>
{
    public const string SystemActor = "System";

    public string? Resolve(object source, object destination, Guid? sourceMember, string? destMember, ResolutionContext context) =>
        sourceMember is null ? SystemActor : directory.GetDisplayName(sourceMember);
}

public sealed class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Claim, ClaimSummaryDto>()
            .ForMember(d => d.LossDate, o => o.MapFrom(s => s.LossEvent.LossDate))
            .ForMember(d => d.CauseOfLossCode, o => o.MapFrom(s => s.LossEvent.CauseOfLossCode))
            .ForMember(d => d.CauseOfLossName, o => o.Ignore())
            .ForMember(d => d.TotalReserves, o => o.MapFrom(s => s.TotalReserves))
            .ForMember(d => d.AssignedHandlerName, o => o.MapFrom<UserNameResolver, Guid?>(s => s.AssignedHandlerId));

        CreateMap<Claim, ClaimDetailDto>()
            .ForMember(d => d.AssignedHandlerName, o => o.MapFrom<UserNameResolver, Guid?>(s => s.AssignedHandlerId))
            .ForMember(d => d.ReserveSummary, o => o.Ignore())
            .ForMember(d => d.RecentAudit, o => o.Ignore())
            .ForMember(d => d.ValidNextStatuses, o => o.Ignore());

        CreateMap<LossEvent, LossEventDto>().ForMember(d => d.CauseOfLossName, o => o.Ignore());
        CreateMap<ClaimParty, ClaimPartyDto>();
        CreateMap<ClaimRiskObject, ClaimRiskObjectDto>();
        CreateMap<ClaimValidationIssue, ValidationIssueDto>();

        CreateMap<ClaimDocument, DocumentDto>()
            .ForMember(d => d.UploadedByName, o => o.MapFrom<UserNameResolver, Guid?>(s => s.UploadedByUserId))
            .ForMember(d => d.DownloadUrl, o => o.Ignore())
            .ForMember(d => d.DownloadUrlExpiresAt, o => o.Ignore());

        CreateMap<ClaimAuditLog, AuditEntryDto>()
            .ForMember(d => d.CreatedByName, o => o.MapFrom<ActorNameResolver, Guid?>(s => s.CreatedByUserId));

        CreateMap<ClaimReserveComponent, ReserveComponentDto>();

        CreateMap<ReserveHistory, ReserveTransactionDto>()
            .ForMember(d => d.Component, o => o.Ignore())
            .ForMember(d => d.SubmittedByName, o => o.MapFrom<UserNameResolver, Guid?>(s => s.SubmittedByUserId))
            .ForMember(d => d.ApprovedByName, o => o.MapFrom<UserNameResolver, Guid?>(s => s.ApprovedByUserId));

        CreateMap<Policy, PolicyDto>()
            .ForMember(d => d.CoverageTypes, o => o.MapFrom(s => s.CoverageTypeList()));
    }
}
