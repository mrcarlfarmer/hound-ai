using Hound.Core.Logging;

namespace Hound.Trading.Hounds;

/// <summary>
/// AF Agent: Determines trading strategy based on market context from AnalysisHound.
/// Outputs strategy decisions (buy/sell/hold, asset, reasoning).
/// </summary>
public class StrategyHound
{
    private readonly IActivityLogger _logger;

    public StrategyHound(IActivityLogger logger)
    {
        _logger = logger;
    }

    // TODO: Implement in Wave 2 — AF Agent with strategy determination logic
}
