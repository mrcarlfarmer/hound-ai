using Hound.Core.Logging;

namespace Hound.Trading.Hounds;

/// <summary>
/// AF Agent: Executes approved trades via Alpaca API. Logs results to activity logger.
/// </summary>
public class ExecutionHound
{
    private readonly IActivityLogger _logger;

    public ExecutionHound(IActivityLogger logger)
    {
        _logger = logger;
    }

    // TODO: Implement in Wave 2 — AF Agent with Alpaca order execution tools
}
