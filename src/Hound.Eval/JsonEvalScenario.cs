using System.Text.Json.Serialization;

namespace Hound.Eval;

/// <summary>
/// Concrete evaluation scenario loaded from a JSON file. Implements <see cref="IEvalScenario"/>.
/// </summary>
public class JsonEvalScenario : IEvalScenario
{
    [JsonPropertyName("scenarioName")]
    public string ScenarioName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("houndName")]
    public string HoundName { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("input")]
    public EvalInput Input { get; set; } = new();

    [JsonPropertyName("expectedBehavior")]
    public EvalExpectedBehavior ExpectedBehavior { get; set; } = new();

    [JsonPropertyName("scoring")]
    public EvalScoring Scoring { get; set; } = new();
}
