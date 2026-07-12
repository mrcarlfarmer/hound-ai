using Hound.Core.Logging;
using Hound.Grocery.Config;
using Hound.Grocery.Graph;
using Hound.Grocery.Nodes;
using Hound.Grocery.Services;
using Microsoft.Extensions.Options;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hound.Grocery.Eval;

/// <summary>
/// Loads grocery scenario JSON files, runs them against the grocery hounds, and
/// scores responses. Unlike the trading harness this runs FULLY OFFLINE: the
/// deterministic hounds (Budget/Learner/Shopper) need no LLM, and the LLM/embedding
/// seams of the Concierge/Planner hounds are replaced with context-driven stubs.
/// So both <c>--dry-run</c> and a full eval run work with no Ollama or network.
/// </summary>
public class EvalRunner
{
    private readonly string _scenariosDir;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public EvalRunner(string? scenariosDir = null)
    {
        _scenariosDir = scenariosDir
            ?? Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                "Scenarios");
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

    /// <summary>Runs all matching scenarios against the grocery hounds (offline).</summary>
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

            if (result.Pass) report.Passed++;
            else report.Failed++;

            if (!report.PerHound.TryGetValue(scenario.HoundName, out var summary))
            {
                summary = new HoundEvalSummary();
                report.PerHound[scenario.HoundName] = summary;
            }

            summary.Total++;
            if (result.Pass) summary.Passed++;
            else summary.Failed++;

            if (verbose)
            {
                var icon = result.Pass ? "\u2713" : "\u2717";
                Console.WriteLine($"  [{icon}] {scenario.HoundName}/{scenario.ScenarioName} ({scenario.Category})");
                Console.WriteLine($"        Reason : {result.Reason}");
                if (result.Output is not null)
                    Console.WriteLine($"        Output : {result.Output[..Math.Min(200, result.Output.Length)]}...");
            }
        }

        return report;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<ScenarioResult> RunScenarioAsync(IEvalScenario scenario, CancellationToken ct)
    {
        try
        {
            var output = await InvokeHoundAsync(scenario, ct);
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
                Reason = $"Exception: {ex.GetType().Name} \u2014 {ex.Message}",
                Input = scenario.Input.UserMessage,
            };
        }
    }

    private async Task<string> InvokeHoundAsync(IEvalScenario scenario, CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "hound-grocery-eval", Guid.NewGuid().ToString("N"));
        var stateFiles = new StateFileService(Options.Create(new StateFileSettings { DataDirectory = tempDir }));
        IActivityLogger log = new NullActivityLogger();

        try
        {
            return Normalise(scenario.HoundName) switch
            {
                "conciergehound" => await RunConciergeAsync(scenario, stateFiles, log, ct),
                "plannerhound" => await RunPlannerAsync(scenario, stateFiles, log, ct),
                "shopperhound" => await RunShopperAsync(scenario, stateFiles, log, ct),
                "budgethound" => RunBudget(scenario, stateFiles),
                "learnerhound" => await RunLearnerAsync(scenario, stateFiles, ct),
                _ => throw new NotSupportedException($"Unknown grocery hound: '{scenario.HoundName}'"),
            };
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    private static async Task<string> RunConciergeAsync(
        IEvalScenario scenario, StateFileService stateFiles, IActivityLogger log, CancellationToken ct)
    {
        var ctx = DeserializeContext<ConciergeEvalContext>(scenario.Input.Context) ?? new ConciergeEvalContext();
        var shoppingList = new ShoppingListService(stateFiles);
        foreach (var item in ctx.ExistingList ?? [])
            await shoppingList.AddOrUpdateAsync(item.Name, item.Quantity, "seed", new DateTime(2026, 6, 1), ct);

        var intents = ctx.Intents is { Count: > 0 } ? ctx.Intents : [new ConciergeIntent(ConciergeIntentKind.Unknown)];
        var parser = new StubShoppingListParser(new ShoppingListParseResult(intents));
        var hound = new ConciergeHound(log, parser, shoppingList, Options.Create(new TelegramSettings { PersonaName = "Chef" }));

        var reply = await hound.HandleMessageAsync(
            new TelegramIncomingMessage(1, 1, "Carl", scenario.Input.UserMessage, 1), ct);
        var list = await shoppingList.RenderSummaryAsync(ct);

        return JsonSerializer.Serialize(new { reply.Text, reply.ListChanged, List = list }, JsonOptions);
    }

    private static async Task<string> RunPlannerAsync(
        IEvalScenario scenario, StateFileService stateFiles, IActivityLogger log, CancellationToken ct)
    {
        var ctx = DeserializeContext<PlannerEvalContext>(scenario.Input.Context) ?? new PlannerEvalContext();
        var shoppingList = new ShoppingListService(stateFiles);
        foreach (var item in ctx.ListItems ?? [])
            await shoppingList.AddOrUpdateAsync(item.Name, item.Quantity, "seed", new DateTime(2026, 6, 1), ct);

        if (!string.IsNullOrWhiteSpace(ctx.Preferences))
            await stateFiles.WriteAsync(GroceryStateFile.Preferences, ctx.Preferences, ct);

        var preferences = new PreferenceService(stateFiles);
        var matcher = new StubItemMatcher(ctx.Match is { } m ? new ItemMatch(m.Candidate, m.Score) : null);
        var assistant = new StubPlannerAssistant(ctx.Suggestion ?? new PlanSuggestion(string.Empty, null, 1));
        var hound = new PlannerHound(log, shoppingList, preferences, matcher, assistant,
            Options.Create(new BudgetSettings { WeeklyTarget = ctx.WeeklyTarget }));

        var next = await hound.ExecuteAsync(GroceryGraphState.Initial(), ct);
        return JsonSerializer.Serialize(next.Plan, JsonOptions);
    }

    private static async Task<string> RunShopperAsync(
        IEvalScenario scenario, StateFileService stateFiles, IActivityLogger log, CancellationToken ct)
    {
        var ctx = DeserializeContext<ShopperEvalContext>(scenario.Input.Context) ?? new ShopperEvalContext();
        var byTerm = (ctx.Candidates ?? [])
            .ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<ProductCandidate>)(kv.Value ?? []).Select(c => c.ToCandidate()).ToList());
        var browser = new StubBrowserWorkerClient(byTerm, ctx.Authenticated);
        var hound = new ShopperHound(log, browser, new ProductRanker(),
            new BasketTraceService(stateFiles), new PurchaseHistoryService(stateFiles));

        var plan = new ShoppingPlan(ctx.Items ?? []);
        var next = await hound.ExecuteAsync(GroceryGraphState.Initial() with { Plan = plan }, ct);
        return JsonSerializer.Serialize(next.Basket, JsonOptions);
    }

    private static string RunBudget(IEvalScenario scenario, StateFileService stateFiles)
    {
        var ctx = DeserializeContext<BudgetEvalContext>(scenario.Input.Context) ?? new BudgetEvalContext();
        var ledger = new BudgetLedgerService(
            Options.Create(new BudgetSettings
            {
                CycleStartDay = ctx.CycleStartDay,
                WeeklyTarget = ctx.WeeklyTarget,
                WeeklyFlexPercent = ctx.WeeklyFlexPercent,
                MonthlyCap = ctx.MonthlyCap,
            }),
            stateFiles);

        var verdict = ledger.EvaluateBasket(ctx.BasketSubtotal, ctx.PriorWeekSpend, ctx.PriorCycleSpend);
        DateOnly? weekStart = DateOnly.TryParse(ctx.AsOf, out var asOf) ? ledger.CurrentWeekStart(asOf) : null;

        return JsonSerializer.Serialize(new { verdict, currentWeekStart = weekStart }, JsonOptions);
    }

    private static async Task<string> RunLearnerAsync(
        IEvalScenario scenario, StateFileService stateFiles, CancellationToken ct)
    {
        var ctx = DeserializeContext<LearnerEvalContext>(scenario.Input.Context) ?? new LearnerEvalContext();
        if (!string.IsNullOrWhiteSpace(ctx.ExistingPreferences))
            await stateFiles.WriteAsync(GroceryStateFile.Preferences, ctx.ExistingPreferences, ct);

        var preferences = new PreferenceService(stateFiles);
        var existing = await preferences.LoadAsync(ct);
        var learner = new PreferenceLearner(Options.Create(new LearnerSettings()));

        var result = learner.Learn(existing, ctx.Observations ?? [], ctx.Dislikes);
        var rendered = PreferencesSerializer.Render(result.Document);

        return JsonSerializer.Serialize(new { result.Changes, Preferences = rendered }, JsonOptions);
    }

    private static T? DeserializeContext<T>(Dictionary<string, object>? context)
    {
        if (context is null) return default;
        var json = JsonSerializer.Serialize(context, JsonOptions);
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private static string Normalise(string houndName) =>
        new string(houndName.Where(char.IsLetter).ToArray()).ToLowerInvariant();

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

    // ── Context input DTOs (deserialized from scenario `context`) ────────────

    private sealed record ConciergeEvalContext(
        List<ConciergeIntent>? Intents = null,
        List<ListItemDto>? ExistingList = null);

    private sealed record PlannerEvalContext(
        List<ListItemDto>? ListItems = null,
        string? Preferences = null,
        PlanSuggestion? Suggestion = null,
        MatchDto? Match = null,
        decimal WeeklyTarget = 140m);

    private sealed record ShopperEvalContext(
        List<PlannedItem>? Items = null,
        Dictionary<string, List<CandidateDto>>? Candidates = null,
        bool Authenticated = true);

    private sealed record BudgetEvalContext(
        decimal BasketSubtotal = 0m,
        decimal PriorWeekSpend = 0m,
        decimal PriorCycleSpend = 0m,
        decimal WeeklyTarget = 140m,
        int WeeklyFlexPercent = 10,
        decimal MonthlyCap = 600m,
        int CycleStartDay = 25,
        string? AsOf = null);

    private sealed record LearnerEvalContext(
        List<PurchaseObservation>? Observations = null,
        List<string>? Dislikes = null,
        string? ExistingPreferences = null);

    private sealed record ListItemDto(string Name, double Quantity = 1);

    private sealed record MatchDto(string Candidate, double Score);

    private sealed record CandidateDto(
        string ProductId,
        string Name,
        decimal Price,
        decimal? NectarPrice = null,
        bool IsNectarPrice = false,
        bool IsFavourite = false,
        bool InStock = true,
        string? PerUnitPrice = null,
        string? Url = null,
        string? ImgRef = null)
    {
        public ProductCandidate ToCandidate() => new(
            ProductId, Name, Price, NectarPrice, IsNectarPrice, IsFavourite, InStock,
            PerUnitPrice, Url ?? $"/groceries/product/{ProductId}", ImgRef);
    }
}
