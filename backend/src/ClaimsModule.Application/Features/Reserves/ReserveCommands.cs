using AutoMapper;
using ClaimsModule.Application.Abstractions;
using ClaimsModule.Application.Common;
using ClaimsModule.Application.Features.Claims;
using ClaimsModule.Application.Features.Claims.Commands;
using ClaimsModule.Application.Features.Claims.Dtos;
using ClaimsModule.Domain.Entities;
using ClaimsModule.Domain.Enums;
using ClaimsModule.Domain.Rules;
using FluentValidation;
using MediatR;

namespace ClaimsModule.Application.Features.Reserves;

// ------------------------------------------------------------------ open / adjust

/// <summary>Opens a reserve component or changes it. The transaction type decides the direction (Reverse reduces).</summary>
public sealed record SubmitReserveCommand(
    Guid ClaimId,
    ReserveComponentType Component,
    decimal Amount,
    string ChangeReason,
    ReserveTransactionType TransactionType = ReserveTransactionType.Add) : ICommand<ReserveSubmissionDto>;

public sealed class SubmitReserveValidator : AbstractValidator<SubmitReserveCommand>
{
    public SubmitReserveValidator()
    {
        RuleFor(c => c.Component).IsInEnum().WithMessage(ValidationMessages.ReserveComponent);
        RuleFor(c => c.TransactionType).IsInEnum().WithMessage("Invalid reserve transaction type.");
        RuleFor(c => c).Must(c => ReserveRules.IsValidAmount(c.Component, c.Amount))
            .WithMessage(ValidationMessages.ReserveAmount).WithName(nameof(SubmitReserveCommand.Amount))
            .When(c => Enum.IsDefined(c.Component));
        RuleFor(c => c.ChangeReason).NotEmpty().WithMessage("A change reason is required.").MaximumLength(500);
    }
}

public sealed class SubmitReserveHandler(IClaimRepository claims, ICurrentUserService currentUser, TimeProvider time, IMapper mapper)
    : IRequestHandler<SubmitReserveCommand, ReserveSubmissionDto>
{
    public async Task<ReserveSubmissionDto> Handle(SubmitReserveCommand cmd, CancellationToken ct)
    {
        var (userId, _) = currentUser.Require();
        var claim = await claims.GetByIdAsync(cmd.ClaimId, ClaimIncludes.ForUpdate, track: true, ct)
            ?? throw new NotFoundException(nameof(Claim), cmd.ClaimId);

        var result = claim.SubmitReserve(cmd.Component, cmd.TransactionType, cmd.Amount, cmd.ChangeReason, time.GetUtcNow(), userId);
        return ClaimDtoFactory.ToSubmissionDto(claim, result, mapper);
    }
}

/// <summary>PUT /reserves/{reserveId}: adjusts an existing component (always recorded as an Adjust transaction).</summary>
public sealed record AdjustReserveCommand(Guid ClaimId, Guid ReserveComponentId, decimal Amount, string ChangeReason)
    : ICommand<ReserveSubmissionDto>;

public sealed class AdjustReserveValidator : AbstractValidator<AdjustReserveCommand>
{
    public AdjustReserveValidator()
    {
        RuleFor(c => c.Amount).NotEqual(0).WithMessage(ValidationMessages.ReserveAmount);
        RuleFor(c => c.ChangeReason).NotEmpty().WithMessage("A change reason is required.").MaximumLength(500);
    }
}

public sealed class AdjustReserveHandler(IClaimRepository claims, ICurrentUserService currentUser, TimeProvider time, IMapper mapper)
    : IRequestHandler<AdjustReserveCommand, ReserveSubmissionDto>
{
    public async Task<ReserveSubmissionDto> Handle(AdjustReserveCommand cmd, CancellationToken ct)
    {
        var (userId, _) = currentUser.Require();
        var claim = await claims.GetByIdAsync(cmd.ClaimId, ClaimIncludes.ForUpdate, track: true, ct)
            ?? throw new NotFoundException(nameof(Claim), cmd.ClaimId);
        var component = claim.ReserveComponents.FirstOrDefault(c => c.Id == cmd.ReserveComponentId)
            ?? throw new NotFoundException(nameof(ClaimReserveComponent), cmd.ReserveComponentId);

        // Validator guarantees non-zero; a non-subrogation component additionally needs a positive amount.
        if (!ReserveRules.IsValidAmount(component.Component, cmd.Amount))
        {
            throw new RequestValidationException(nameof(cmd.Amount), ValidationMessages.ReserveAmount);
        }

        var result = claim.SubmitReserve(component.Component, ReserveTransactionType.Adjust, cmd.Amount, cmd.ChangeReason, time.GetUtcNow(), userId);
        return ClaimDtoFactory.ToSubmissionDto(claim, result, mapper);
    }
}

// ------------------------------------------------------------------ approve / reject / retract

public sealed record ApproveReserveCommand(Guid ClaimId, Guid TransactionId) : ICommand<ReserveTransactionDto>;

public sealed class ApproveReserveHandler(IClaimRepository claims, ICurrentUserService currentUser, TimeProvider time, IMapper mapper)
    : IRequestHandler<ApproveReserveCommand, ReserveTransactionDto>
{
    public async Task<ReserveTransactionDto> Handle(ApproveReserveCommand cmd, CancellationToken ct)
    {
        var (userId, role) = currentUser.Require();
        var claim = await claims.LoadForUpdateAsync(cmd.ClaimId, ct);
        var txn = claim.ApproveReserve(cmd.TransactionId, userId, role, time.GetUtcNow());
        return ClaimDtoFactory.ToTransactionDto(claim, txn, mapper);
    }
}

public sealed record RejectReserveCommand(Guid ClaimId, Guid TransactionId, string RejectionReason) : ICommand<ReserveTransactionDto>;

public sealed class RejectReserveValidator : AbstractValidator<RejectReserveCommand>
{
    public RejectReserveValidator() =>
        RuleFor(c => c.RejectionReason).NotEmpty().WithMessage("A rejection reason is required.").MaximumLength(1000);
}

public sealed class RejectReserveHandler(IClaimRepository claims, ICurrentUserService currentUser, TimeProvider time, IMapper mapper)
    : IRequestHandler<RejectReserveCommand, ReserveTransactionDto>
{
    public async Task<ReserveTransactionDto> Handle(RejectReserveCommand cmd, CancellationToken ct)
    {
        var (userId, role) = currentUser.Require();
        var claim = await claims.LoadForUpdateAsync(cmd.ClaimId, ct);
        var txn = claim.RejectReserve(cmd.TransactionId, userId, role, cmd.RejectionReason, time.GetUtcNow());
        return ClaimDtoFactory.ToTransactionDto(claim, txn, mapper);
    }
}

public sealed record RetractReserveCommand(Guid ClaimId, Guid TransactionId) : ICommand<ReserveTransactionDto>;

public sealed class RetractReserveHandler(IClaimRepository claims, ICurrentUserService currentUser, IMapper mapper)
    : IRequestHandler<RetractReserveCommand, ReserveTransactionDto>
{
    public async Task<ReserveTransactionDto> Handle(RetractReserveCommand cmd, CancellationToken ct)
    {
        var (userId, _) = currentUser.Require();
        var claim = await claims.LoadForUpdateAsync(cmd.ClaimId, ct);
        var txn = claim.RetractReserve(cmd.TransactionId, userId);
        return ClaimDtoFactory.ToTransactionDto(claim, txn, mapper);
    }
}

/// <summary>Re-queues a GL posting that failed after all Hangfire retries.</summary>
public sealed record RetryGlPostingCommand(Guid ClaimId, Guid TransactionId) : ICommand<ReserveTransactionDto>;

public sealed class RetryGlPostingHandler(IClaimRepository claims, IMapper mapper)
    : IRequestHandler<RetryGlPostingCommand, ReserveTransactionDto>
{
    public async Task<ReserveTransactionDto> Handle(RetryGlPostingCommand cmd, CancellationToken ct)
    {
        var claim = await claims.LoadForUpdateAsync(cmd.ClaimId, ct);
        var txn = claim.RetryGlPosting(cmd.TransactionId);
        return ClaimDtoFactory.ToTransactionDto(claim, txn, mapper);
    }
}

// ------------------------------------------------------------------ query

public sealed record GetReservesQuery(Guid ClaimId) : IQuery<ReservesDto>;

public sealed class GetReservesHandler(IClaimRepository claims, IMapper mapper) : IRequestHandler<GetReservesQuery, ReservesDto>
{
    public async Task<ReservesDto> Handle(GetReservesQuery query, CancellationToken ct)
    {
        var claim = await claims.GetByIdAsync(query.ClaimId, ClaimIncludes.Reserves, track: false, ct)
            ?? throw new NotFoundException(nameof(Claim), query.ClaimId);
        return new ReservesDto(ClaimDtoFactory.BuildReserveSummary(claim, mapper), ClaimDtoFactory.BuildTransactions(claim, mapper));
    }
}
