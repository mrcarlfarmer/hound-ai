using Hound.Core.Logging;

namespace Hound.Trading.Hounds;

/// <summary>
/// AF Agent: Analyses market data (price bars, volume, indicators).
/// Produces recommendations with confidence scores.
/// </summary>
public class AnalysisHound
{
    private readonly IActivityLogger _logger;

    public AnalysisHound(IActivityLogger logger)
    {
        _logger = logger;
    }

    // TODO: Implement in Wave 2 — AF Agent with market data tools via AlpacaService
}
