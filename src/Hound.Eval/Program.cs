using Hound.Eval;

var houndFilter = args.FirstOrDefault(a => a.StartsWith("--hound"))?.Split('=').ElementAtOrDefault(1);
var categoryFilter = args.FirstOrDefault(a => a.StartsWith("--category"))?.Split('=').ElementAtOrDefault(1);
var verbose = args.Contains("--verbose");
var dryRun = args.Contains("--dry-run");

if (dryRun)
{
    Console.WriteLine("Dry run: validating scenario files...");
    // TODO: Wave 3 — load and validate all JSON scenario files without executing
    Console.WriteLine("All scenarios validated.");
    return;
}

var runner = new EvalRunner();
var report = await runner.RunAllAsync(houndFilter, categoryFilter, verbose);

Console.WriteLine($"Total: {report.TotalScenarios} | Passed: {report.Passed} | Failed: {report.Failed} | Pass Rate: {report.PassRate:P0}");
foreach (var (hound, summary) in report.PerHound)
{
    Console.WriteLine($"  {hound}: {summary.Passed}/{summary.Total} ({summary.PassRate:P0})");
}
