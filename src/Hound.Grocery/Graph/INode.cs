namespace Hound.Grocery.Graph;

/// <summary>
/// A single node ("hound") in the grocery graph. Each node performs one step of
/// the pack pipeline (intake, planning, shopping, budgeting, learning) and
/// returns the updated shared state.
/// <para>
/// Mirrors <c>Hound.Trading.Graph.INode</c>; the interface is pack-local so the
/// grocery pack never shares in-process state with the trading pack.
/// </para>
/// </summary>
public interface INode
{
    string NodeId { get; }
    string PackId { get; }
    Task<GroceryGraphState> ExecuteAsync(GroceryGraphState state, CancellationToken cancellationToken);
}
