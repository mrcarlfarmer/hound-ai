using Hound.Core.Logging;

namespace Hound.Trading.Hounds;

/// <summary>
/// AF Agent: Risk management. Evaluates proposed trades against portfolio exposure,
/// position limits, max drawdown rules. Approves/rejects/modifies orders.
/// </summary>
public class RiskHound
{
    private readonly IActivityLogger _logger;

    public RiskHound(IActivityLogger logger)
    {
        _logger = logger;
    }

    // TODO: Implement in Wave 2 — AF Agent with risk evaluation tools
}
