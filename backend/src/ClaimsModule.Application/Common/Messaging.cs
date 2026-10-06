using MediatR;

namespace ClaimsModule.Application.Common;

/// <summary>A state-changing request. Runs inside a transaction; domain events are dispatched before commit.</summary>
public interface ICommand<out TResponse> : IRequest<TResponse> { }

/// <summary>A read-only request. Never opens a transaction or saves changes.</summary>
public interface IQuery<out TResponse> : IRequest<TResponse> { }

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
