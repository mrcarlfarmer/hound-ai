using Hound.Trading.Graph;

namespace Hound.Eval;

/// <summary>
/// No-op resettable executor for eval scenarios. Does not unload models.
/// </summary>
public class StubResettableExecutor : IResettableExecutor
{
    public Task ResetAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
