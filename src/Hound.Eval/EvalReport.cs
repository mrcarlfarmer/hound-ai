namespace Hound.Eval;

/// <summary>
/// Collects results from evaluation runs and outputs summary.
/// </summary>
public class EvalReport
{
    public int TotalScenarios { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public double PassRate => TotalScenarios == 0 ? 0 : (double)Passed / TotalScenarios;
    public Dictionary<string, HoundEvalSummary> PerHound { get; set; } = new();
    public List<ScenarioResult> Results { get; set; } = [];
}

public class HoundEvalSummary
{
    public int Total { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public double PassRate => Total == 0 ? 0 : (double)Passed / Total;
}

public class ScenarioResult
{
    public string ScenarioName { get; set; } = string.Empty;
    public string HoundName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool Pass { get; set; }
    public string? Reason { get; set; }
    public string? Input { get; set; }
    public string? Output { get; set; }
}
