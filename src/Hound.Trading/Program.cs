var builder = Host.CreateApplicationBuilder(args);

// TODO: Wave 2 — Register services:
// - IDocumentStore (RavenDB)
// - IOllamaClientFactory
// - IAlpacaService
// - IActivityLogger
// - Trading hounds (AnalysisHound, StrategyHound, RiskHound, ExecutionHound)
// - TradingWorkflow
// - IHostedService for scheduled workflow execution

var host = builder.Build();
host.Run();
