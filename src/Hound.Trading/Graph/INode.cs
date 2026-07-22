namespace Hound.Trading.Graph;

/// <summary>
/// A single node in the trading graph. Each node performs one step
/// of the pipeline (data gathering, strategy, risk, execution, monitoring)
/// and returns the updated shared state.
/// </summary>
public interface INode
{
    string NodeId { get; }
    string PackId { get; }
    Task<TradingGraphState> ExecuteAsync(TradingGraphState state, CancellationToken cancellationToken);
}
