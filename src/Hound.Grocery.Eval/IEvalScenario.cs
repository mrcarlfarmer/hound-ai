namespace Hound.Grocery.Eval;

/// <summary>
/// Defines a single evaluation scenario for a grocery hound agent. Mirrors the
/// trading harness's contract (<c>Hound.Eval</c>) so the two suites share the
/// same scenario JSON schema and skill conventions.
/// </summary>
public interface IEvalScenario
{
    string ScenarioName { get; }
    string Description { get; }
    string HoundName { get; }
    string Category { get; } // happy-path, edge-case, adversarial, tool-usage, refusal
    EvalInput Input { get; }
    EvalExpectedBehavior ExpectedBehavior { get; }
    EvalScoring Scoring { get; }
}

public class EvalInput
{
    public string UserMessage { get; set; } = string.Empty;
    public Dictionary<string, object>? Context { get; set; }
}

public class EvalExpectedBehavior
{
    public List<string> ShouldCallTools { get; set; } = [];
    public List<string> OutputMustContain { get; set; } = [];
    public List<string> OutputMustNotContain { get; set; } = [];
    public string? OutputFormat { get; set; }
    public string? DecisionCriteria { get; set; }
}

public class EvalScoring
{
    public string Type { get; set; } = "binary"; // binary or rubric
    public string PassCriteria { get; set; } = string.Empty;
}
