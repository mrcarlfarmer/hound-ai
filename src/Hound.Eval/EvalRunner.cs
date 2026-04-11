namespace Hound.Eval;

/// <summary>
/// Loads scenario JSON files, runs them against hound agents, and scores responses.
/// </summary>
public class EvalRunner
{
    // TODO: Wave 3 — Implement:
    // - Load scenario JSON files from Scenarios/{HoundName}/
    // - Instantiate target hound using IOllamaClientFactory
    // - Send scenario input to hound via AF Agent.RunAsync
    // - Score response against expected behavior criteria
    // - Support --hound, --category, --verbose flags

    public Task<EvalReport> RunAllAsync(string? houndFilter = null, string? categoryFilter = null, bool verbose = false, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
