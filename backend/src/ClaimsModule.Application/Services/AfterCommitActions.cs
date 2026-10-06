using ClaimsModule.Application.Abstractions;

namespace ClaimsModule.Application.Services;

public sealed class AfterCommitActions : IAfterCommitActions
{
    private readonly List<Func<CancellationToken, Task>> _actions = new();

    public void Add(Func<CancellationToken, Task> action) => _actions.Add(action);

    public void Clear() => _actions.Clear();

    public async Task RunAsync(CancellationToken ct)
    {
        var pending = _actions.ToList();
        _actions.Clear();
        foreach (var action in pending)
        {
            await action(ct);
        }
    }
}
