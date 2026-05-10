using Hound.Core.Logging;
using Hound.Core.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations;
using Raven.Client.Exceptions.Database;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using System.Text.Json;

namespace Hound.Trading.Hounds;

/// <summary>
/// AF Agent: Acts as the portfolio manager for the trading pack.
/// Builds trade proposals from market context and records actionable signals for review.
/// </summary>
public class StrategyHound
{
    private const string HoundId = "strategy-hound";
    private const string PackId = "trading-pack";
    private const string TradingDatabase = "hound-trading-pack";

    private readonly ChatClientAgent _agent;
    private readonly IActivityLogger _activityLogger;
    private readonly IDocumentStore? _documentStore;
    private readonly ILogger<StrategyHound>? _logger;

    public StrategyHound(
        IChatClient chatClient,
        IActivityLogger activityLogger,
        IDocumentStore? documentStore = null,
        ILoggerFactory? loggerFactory = null)
    {
        _activityLogger = activityLogger;
        _documentStore = documentStore;
        _logger = loggerFactory?.CreateLogger<StrategyHound>();

        _agent = new ChatClientAgent(
            chatClient,
            instructions: """
                You are StrategyHound, an institutional portfolio manager.
                Given a world-state snapshot (JSON), decide whether to propose increasing, reducing, or holding exposure.
                You are generating trade proposals for review, not executing orders.
                Consider the trend, confidence score, and volume change.
                - Confidence >= 0.7 and Bullish trend => Buy proposal
                - Confidence >= 0.7 and Bearish trend => Sell proposal
                - Otherwise => Hold
                Respond strictly in JSON matching:
                {"symbol":"...","action":"Buy|Sell|Hold","quantity":0.0,"reasoning":"...","confidence":0.0}
                """,
            name: "StrategyHound",
            description: "Determines portfolio-manager trade proposals based on market analysis",
            loggerFactory: loggerFactory);
    }

    public async Task<TradingDecision> DecideAsync(
        MarketAnalysis analysis,
        CancellationToken cancellationToken = default)
    {
        var worldState = StrategySignalProposalFactory.CreateWorldState(analysis);

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = HoundId,
            HoundName = "StrategyHound",
            Message = $"Determining portfolio proposal for {analysis.Symbol}",
            Severity = ActivitySeverity.Info,
        }, cancellationToken);

        var worldStateJson = JsonSerializer.Serialize(worldState);

        var session = await _agent.CreateSessionAsync(cancellationToken);
        var response = await _agent.RunAsync(
            [new ChatMessage(ChatRole.User, $"World state:\n{worldStateJson}\n\nWhat trade proposal should be recorded?")],
            session,
            cancellationToken: cancellationToken);

        var json = response.Text ?? "{}";
        TradingDecision decision;

        try
        {
            decision = StrategySignalProposalFactory.NormalizeDecision(
                worldState,
                JsonSerializer.Deserialize<TradingDecision>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex,
                "StrategyHound returned invalid JSON for {Symbol}. Falling back to deterministic proposal.",
                analysis.Symbol);

            decision = StrategySignalProposalFactory.CreateDecision(worldState);

            await _activityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = PackId,
                HoundId = HoundId,
                HoundName = "StrategyHound",
                Message = $"Strategy response for {analysis.Symbol} was invalid JSON; used deterministic portfolio-manager fallback",
                Severity = ActivitySeverity.Warning,
            }, cancellationToken);
        }

        var proposal = StrategySignalProposalFactory.CreateProposal(worldState, decision);

        if (proposal is not null)
        {
            await SaveProposalAsync(proposal, cancellationToken);

            await _activityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = PackId,
                HoundId = HoundId,
                HoundName = "StrategyHound",
                Message = $"Recorded proposal {proposal.Action} {proposal.Quantity} {proposal.Symbol} in RavenDB",
                Severity = ActivitySeverity.Success,
            }, cancellationToken);
        }

        await _activityLogger.LogActivityAsync(new ActivityLog
        {
            PackId = PackId,
            HoundId = HoundId,
            HoundName = "StrategyHound",
            Message = $"Decision for {analysis.Symbol}: {decision.Action} (confidence {decision.Confidence:P0})",
            Severity = ActivitySeverity.Success,
        }, cancellationToken);

        return decision;
    }

    private async Task SaveProposalAsync(ProposedTradeSignal proposal, CancellationToken cancellationToken)
    {
        if (_documentStore is null)
        {
            return;
        }

        try
        {
            EnsureDatabaseExists(TradingDatabase);

            using var session = _documentStore.OpenAsyncSession(TradingDatabase);
            await session.StoreAsync(proposal, proposal.Id, cancellationToken);
            session.Advanced.GetMetadataFor(proposal)["@collection"] = "Hounds/Proposed";
            await session.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not persist proposal {ProposalId} for {Symbol}", proposal.Id, proposal.Symbol);

            await _activityLogger.LogActivityAsync(new ActivityLog
            {
                PackId = PackId,
                HoundId = HoundId,
                HoundName = "StrategyHound",
                Message = $"Could not persist proposal for {proposal.Symbol}: {ex.Message}",
                Severity = ActivitySeverity.Warning,
            }, cancellationToken);
        }
    }

    private void EnsureDatabaseExists(string database)
    {
        if (_documentStore is null)
        {
            return;
        }

        try
        {
            _documentStore.Maintenance.ForDatabase(database).Send(new GetStatisticsOperation());
        }
        catch (DatabaseDoesNotExistException)
        {
            _documentStore.Maintenance.Server.Send(new CreateDatabaseOperation(new DatabaseRecord(database)));
        }
    }
}
