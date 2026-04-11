using Hound.Eval;
using System.Text.Json;

var houndFilter = args.FirstOrDefault(a => a.StartsWith("--hound="))?.Split('=').ElementAtOrDefault(1)
    ?? (args.SkipWhile(a => a != "--hound").Skip(1).FirstOrDefault() is { } h && !h.StartsWith("--") ? h : null);

var categoryFilter = args.FirstOrDefault(a => a.StartsWith("--category="))?.Split('=').ElementAtOrDefault(1)
    ?? (args.SkipWhile(a => a != "--category").Skip(1).FirstOrDefault() is { } c && !c.StartsWith("--") ? c : null);

var verbose = args.Contains("--verbose");
var dryRun = args.Contains("--dry-run");

if (dryRun)
{
    Console.WriteLine("Dry run: validating scenario files...");

    var runner = new EvalRunner();
    var errors = new List<string>();

    try
    {
        var scenarios = runner.LoadScenarios(houndFilter, categoryFilter);

        foreach (var scenario in scenarios)
        {
            if (string.IsNullOrWhiteSpace(scenario.ScenarioName))
                errors.Add($"  [{scenario.HoundName}] Scenario has no name");

            if (string.IsNullOrWhiteSpace(scenario.Category))
                errors.Add($"  [{scenario.HoundName}/{scenario.ScenarioName}] Missing category");

            if (string.IsNullOrWhiteSpace(scenario.Input.UserMessage))
                errors.Add($"  [{scenario.HoundName}/{scenario.ScenarioName}] Missing userMessage");

            if (string.IsNullOrWhiteSpace(scenario.Scoring.PassCriteria))
                errors.Add($"  [{scenario.HoundName}/{scenario.ScenarioName}] Missing passCriteria");
        }

        if (errors.Count > 0)
        {
            Console.WriteLine("Validation errors:");
            foreach (var e in errors) Console.WriteLine(e);
            return 1;
        }

        Console.WriteLine($"All {scenarios.Count} scenarios validated successfully.");
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Validation failed: {ex.Message}");
        return 1;
    }
}

var evalRunner = new EvalRunner();
var report = await evalRunner.RunAllAsync(houndFilter, categoryFilter, verbose);

Console.WriteLine();
Console.WriteLine($"=== Eval Results ===");
Console.WriteLine($"Total: {report.TotalScenarios} | Passed: {report.Passed} | Failed: {report.Failed} | Pass Rate: {report.PassRate:P0}");
Console.WriteLine();

foreach (var (hound, summary) in report.PerHound.OrderBy(kv => kv.Key))
{
    var rate = summary.PassRate;
    var icon = rate >= 1.0 ? "✓" : rate >= 0.6 ? "~" : "✗";
    Console.WriteLine($"  [{icon}] {hound}: {summary.Passed}/{summary.Total} ({summary.PassRate:P0})");
}

return report.Failed == 0 ? 0 : 1;

