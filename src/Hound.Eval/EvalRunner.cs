using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.AlpacaClient;
using Hound.Trading.Graph;
using Hound.Trading.Nodes;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hound.Eval;

/// <summary>
/// Loads scenario JSON files, runs them against hound agents, and scores responses.
/// </summary>
public class EvalRunner
{
    private readonly string _scenariosDir;
    private readonly string _ollamaUrl;
    private readonly string _modelName;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public EvalRunner(
        string? scenariosDir = null,
        string? ollamaUrl = null,
        string? modelName = null)
    {
        _scenariosDir = scenariosDir
            ?? Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                "Scenarios");

        _ollamaUrl = ollamaUrl
            ?? Environment.GetEnvironmentVariable("OLLAMA_URL")
            ?? "http://localhost:11434/v1";

        _modelName = modelName
            ?? Environment.GetEnvironmentVariable("OLLAMA_MODEL")
            ?? "gemma3";
    }

    /// <summary>
    /// Loads and parses all scenario JSON files from the Scenarios directory.
    /// Applies optional hound-name and category filters.
    /// </summary>
    public IReadOnlyList<IEvalScenario> LoadScenarios(
        string? houndFilter = null,
        string? categoryFilter = null)
    {
        if (!Directory.Exists(_scenariosDir))
            return [];

        var scenarios = new List<IEvalScenario>();

        foreach (var houndDir in Directory.GetDirectories(_scenariosDir).OrderBy(d => d))
        {
            var houndName = Path.GetFileName(houndDir);

            if (houndFilter is not null &&
                !houndName.Equals(houndFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var file in Directory.GetFiles(houndDir, "*.json").OrderBy(f => f))
            {
                var json = File.ReadAllText(file);
                var scenario = JsonSerializer.Deserialize<JsonEvalScenario>(json, JsonOptions);
                if (scenario is null) continue;

                if (string.IsNullOrWhiteSpace(scenario.HoundName))
                    scenario.HoundName = houndName;

                if (categoryFilter is not null &&
                    !scenario.Category.Equals(categoryFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                scenarios.Add(scenario);
            }
        }

        return scenarios;
    }

    /// <summary>
    /// Runs all matching scenarios, invoking real hound agents via Ollama.
    /// </summary>
    public async Task<EvalReport> RunAllAsync(
        string? houndFilter = null,
        string? categoryFilter = null,
        bool verbose = false,
        CancellationToken cancellationToken = default)
    {
        var scenarios = LoadScenarios(houndFilter, categoryFilter);
        var report = new EvalReport();

        foreach (var scenario in scenarios)
        {
            var result = await RunScenarioAsync(scenario, cancellationToken);
            report.Results.Add(result);
            report.TotalScenarios++;

            if (result.Pass)
                report.Passed++;
            else
                report.Failed++;

            if (!report.PerHound.TryGetValue(scenario.HoundName, out var summary))
            {
                summary = new HoundEvalSummary();
                report.PerHound[scenario.HoundName] = summary;
            }

            summary.Total++;
            if (result.Pass)
                summary.Passed++;
            else
                summary.Failed++;

            if (verbose)
            {
                var icon = result.Pass ? "✓" : "✗";
                Console.WriteLine($"  [{icon}] {scenario.HoundName}/{scenario.ScenarioName} ({scenario.Category})");
                Console.WriteLine($"        Reason : {result.Reason}");
                if (result.Output is not null)
                    Console.WriteLine($"        Output : {result.Output[..Math.Min(200, result.Output.Length)]}...");
            }
        }

        return report;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<ScenarioResult> RunScenarioAsync(
        IEvalScenario scenario,
        CancellationToken cancellationToken)
    {
        try
        {
            var output = await InvokeHoundAsync(scenario, cancellationToken);
            var (pass, reason) = Score(scenario, output);

            return new ScenarioResult
            {
                ScenarioName = scenario.ScenarioName,
                HoundName = scenario.HoundName,
                Category = scenario.Category,
                Pass = pass,
                Reason = reason,
                Input = scenario.Input.UserMessage,
                Output = output,
            };
        }
        catch (Exception ex)
        {
            return new ScenarioResult
            {
                ScenarioName = scenario.ScenarioName,
                HoundName = scenario.HoundName,
                Category = scenario.Category,
                Pass = false,
                Reason = $"Exception: {ex.GetType().Name} — {ex.Message}",
                Input = scenario.Input.UserMessage,
            };
        }
    }

    private async Task<string> InvokeHoundAsync(IEvalScenario scenario, CancellationToken ct)
    {
        var chatClient = CreateChatClient();
        IActivityLogger activityLogger = new NullActivityLogger();
        IAlpacaService alpacaService = new StubAlpacaService();

        switch (scenario.HoundName)
        {
            case "StrategyNode":
            case "StrategyHound":
            {
                var node = new StrategyNode(chatClient, activityLogger);
                var analysis = DeserializeContext<MarketAnalysis>(scenario.Input.Context)
                    ?? new MarketAnalysis("AAPL", 0, 0, "Unknown", 0, scenario.Input.UserMessage);
                var state = TradingGraphState.Initial("AAPL") with { DataOutput = analysis };
                var result = await node.ExecuteAsync(state, ct);
                return JsonSerializer.Serialize(result.StrategyOutput, JsonOptions);
            }

            case "DataNode":
            case "AnalysisHound":
            {
                var node = new DataNode(chatClient, alpacaService, activityLogger);
                var symbol = GetContextString(scenario.Input.Context, "symbol") ?? "AAPL";
                var state = TradingGraphState.Initial(symbol);
                var result = await node.ExecuteAsync(state, ct);
                return JsonSerializer.Serialize(result.DataOutput, JsonOptions);
            }

            case "RiskNode":
            case "RiskHound":
            {
                var node = new RiskNode(chatClient, alpacaService, activityLogger);
                var decision = DeserializeContext<TradingDecision>(scenario.Input.Context)
                    ?? new TradingDecision("AAPL", TradeAction.Buy, 10, scenario.Input.UserMessage, 0.5);
                var state = TradingGraphState.Initial("AAPL") with { StrategyOutput = decision };
                var result = await node.ExecuteAsync(state, ct);
                return JsonSerializer.Serialize(result.RiskOutput, JsonOptions);
            }

            case "ExecutionNode":
            case "ExecutionHound":
            {
                var node = new ExecutionNode(chatClient, alpacaService, activityLogger, StubDocumentStoreFactory.Create());
                var assessment = DeserializeContext<RiskAssessment>(scenario.Input.Context)
                    ?? new RiskAssessment(RiskVerdict.Rejected,
                        new TradingDecision("AAPL", TradeAction.Buy, 10, "", 0),
                        "No assessment provided");
                var state = TradingGraphState.Initial("AAPL") with { RiskOutput = assessment };
                var result = await node.ExecuteAsync(state, ct);
                return JsonSerializer.Serialize(result.ExecutionOutput, JsonOptions);
            }

            case "MonitorNode":
            {
                var resetter = new StubResettableExecutor();
                var node = new MonitorNode(chatClient, alpacaService, activityLogger,
                    StubDocumentStoreFactory.Create(), resetter, monitorDelaySeconds: 0);
                var executionResult = DeserializeContext<ExecutionResult>(scenario.Input.Context)
                    ?? new ExecutionResult(true, "AAPL", TradeAction.Buy, 10, null, "test-order", "Test");
                var state = TradingGraphState.Initial("AAPL") with
                {
                    ExecutionOutput = executionResult,
                    Phase = GraphPhase.Monitor,
                };
                var result = await node.ExecuteAsync(state, ct);
                return JsonSerializer.Serialize(result.MonitorOutput, JsonOptions);
            }

            default:
                throw new NotSupportedException($"Unknown hound: '{scenario.HoundName}'");
        }
    }

    private static T? DeserializeContext<T>(Dictionary<string, object>? context)
    {
        if (context is null) return default;
        var json = JsonSerializer.Serialize(context, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static string? GetContextString(Dictionary<string, object>? context, string key)
    {
        if (context is null || !context.TryGetValue(key, out var value)) return null;
        return value?.ToString();
    }

    private IChatClient CreateChatClient()
    {
        var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(_ollamaUrl) };
        var credential = new ApiKeyCredential("ollama");
        var client = new OpenAI.Chat.ChatClient(_modelName, credential, clientOptions);
        return client.AsIChatClient();
    }

    private static (bool pass, string reason) Score(IEvalScenario scenario, string output)
    {
        var outputLower = output.ToLowerInvariant();

        foreach (var keyword in scenario.ExpectedBehavior.OutputMustContain)
        {
            if (!outputLower.Contains(keyword.ToLowerInvariant()))
                return (false, $"Missing required keyword: '{keyword}'");
        }

        foreach (var forbidden in scenario.ExpectedBehavior.OutputMustNotContain)
        {
            if (outputLower.Contains(forbidden.ToLowerInvariant()))
                return (false, $"Contains forbidden keyword: '{forbidden}'");
        }

        return (true, "All criteria met");
    }
}

