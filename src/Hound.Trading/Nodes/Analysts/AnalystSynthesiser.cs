using Hound.Core.Logging;
using Hound.Core.Models;
using Hound.Trading.Graph;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Hound.Trading.Nodes.Analysts;

/// <summary>
/// Synthesises the four analyst reports into a single structured
/// <see cref="MarketAnalysis"/>. Owns the JSON-formatter agent, its
/// per-call temperature override, the timeout-cancellation fallback, and
/// the schema parsing.
/// </summary>
public sealed class AnalystSynthesiser
{
    private const string PackId = "trading-pack";
    private const string NodeId = "analysts-team-node";
    private const string HoundName = "AnalystsTeam";

    private readonly ChatClientAgent _agent;
    private readonly IActivityLogger _activityLogger;

    public AnalystSynthesiser(
        IChatClient chatClient,
        IActivityLogger activityLogger,
        ILoggerFactory? loggerFactory = null)
    {
        _activityLogger = activityLogger;

        _agent = new ChatClientAgent(
            chatClient,
            instructions: """
                /no_think
                You are a JSON formatter. You receive four analyst reports and extract key metrics.
                You MUST respond with ONLY a single JSON object — no markdown, no explanation, no preamble.
                The JSON schema is:
                {"symbol":"<TICKER>","lastPrice":<number>,"volumeChange":<number>,"trend":"<Bullish|Bearish|Neutral>","confidenceScore":<number>,"summary":"<text>"}

                Rules:
                - lastPrice = the most recent closing price from the market report. Must be > 0.
                - volumeChange = volume ratio from the market report (e.g. 1.2 means 20% above average). Must be > 0.
                - trend = exactly one of: "Bullish", "Bearish", "Neutral"
                - confidenceScore = a value between 0.05 and 1.0. NEVER emit 0; if signals are weak, use 0.25.

                  STEP 1 — Count how many analysts ACTUALLY HAVE A DIRECTIONAL SIGNAL.
                  An analyst report that says it has "no data", "no messages", "no articles",
                  "unable to assess", or otherwise abstains from a directional call is NOT
                  counted. Do NOT count an abstention as Neutral. Only Bullish / Bearish /
                  Neutral calls supported by cited evidence count as a signal. Call this
                  count N (1 to 4). The number of those analysts that agree with the
                  majority direction is A.

                  STEP 2 — Map A/N to a confidence band:
                    * A/N = 1.00 (all signalling analysts agree)        → 0.85–1.00
                    * A/N ≥ 0.66 (clear majority)                        → 0.60–0.84
                    * A/N ≥ 0.50 (split)                                 → 0.35–0.59
                    * A/N < 0.50 (no majority)                           → 0.10–0.34

                  STEP 3 — If N < 4 (one or more analysts abstained), multiply the chosen
                  value by N/4 only when N == 1. Otherwise leave it as-is: missing data
                  should not be double-counted against confidence when the remaining
                  analysts give a clear, evidence-backed reading.

                  Adjust within the sub-range based on the strength of evidence cited.

                - summary = 1-3 sentences combining all four reports. Keep it under 200 words.
                  If an analyst abstained, mention it briefly (e.g. "no sentiment data available").
                - LANGUAGE: All string values (especially `summary` and `trend`) MUST be in English.
                  Do NOT emit Chinese, Mandarin, or any other non-English characters. Prices are in
                  US dollars (USD, $). Do NOT use yuan/元, euro, or any other currency symbol.
                - Output raw JSON only. No ```json fences, no markdown, no extra text.
                """,
            name: "Synthesiser",
            description: "Synthesises analyst reports into a final assessment",
            loggerFactory: loggerFactory);
    }

    /// <summary>
    /// Runs the JSON-formatter agent against a composed prompt with a
    /// wall-clock budget. On timeout, a Neutral / no-confidence fallback
    /// <see cref="MarketAnalysis"/> is returned so the graph can continue.
    /// </summary>
    public async Task<MarketAnalysis> SynthesiseAsync(
        string symbol, string prompt, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var session = await _agent.CreateSessionAsync(cancellationToken);

        // Bump temperature slightly above the global default (0.0). Pure
        // greedy decoding on a JSON-formatter prompt is prone to repetition
        // loops when the analyst reports contain noisy or multilingual text;
        // a small amount of stochasticity reliably breaks them without
        // meaningfully hurting JSON validity.
        var options = new ChatClientAgentRunOptions
        {
            ChatOptions = new ChatOptions { Temperature = 0.2f },
        };

        string text;
        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            cts.CancelAfter(timeout);
            try
            {
                var response = await _agent.RunAsync(
                    [new ChatMessage(ChatRole.User, prompt)],
                    session,
                    options: options,
                    cancellationToken: cts.Token);
                text = response.Text ?? "{}";
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                await _activityLogger.LogActivityAsync(new ActivityLog
                {
                    PackId = PackId,
                    HoundId = NodeId,
                    HoundName = HoundName,
                    Message = $"Synthesiser exceeded {timeout.TotalSeconds:F0}s budget for "
                        + $"{symbol}; aborting and falling back to data-derived signal.",
                    Severity = ActivitySeverity.Warning,
                }, cancellationToken);
                text = "{}";
            }
        }

        var json = LlmResponseParser.ExtractJson(text);
        return ParseSynthesisJson(json, symbol);
    }

    private static MarketAnalysis ParseSynthesisJson(string json, string symbol)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            decimal? lastPrice = root.TryGetProperty("lastPrice", out var lp) && lp.ValueKind == JsonValueKind.Number
                ? lp.GetDecimal() : null;
            decimal? volumeChange = root.TryGetProperty("volumeChange", out var vc) && vc.ValueKind == JsonValueKind.Number
                ? vc.GetDecimal() : null;
            string trend = root.TryGetProperty("trend", out var tr) && tr.ValueKind == JsonValueKind.String
                ? NormalizeTrend(tr.GetString()) : "Neutral";
            double? confidence = root.TryGetProperty("confidenceScore", out var cs) && cs.ValueKind == JsonValueKind.Number
                ? cs.GetDouble() : (double?)null;
            // Treat a zero confidence as "no signal" rather than "strongly low",
            // otherwise the synthesiser silently aborts the graph whenever the
            // LLM omits or defaults the field.
            if (confidence is 0d) confidence = null;
            string summary = root.TryGetProperty("summary", out var su) && su.ValueKind == JsonValueKind.String
                ? su.GetString() ?? "No summary" : "No summary";

            return new MarketAnalysis(symbol, lastPrice, volumeChange, trend, confidence, summary);
        }
        catch
        {
            return new MarketAnalysis(symbol, null, null, "Neutral", null, json);
        }
    }

    /// <summary>
    /// Collapses the LLM's free-form trend label into one of three canonical
    /// values — <c>"Bullish"</c>, <c>"Bearish"</c>, or <c>"Neutral"</c> — so the
    /// dashboard can render a clean badge regardless of what the model emits.
    /// </summary>
    private static string NormalizeTrend(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Neutral";
        var lower = raw.ToLowerInvariant();
        if (lower.Contains("bull")) return "Bullish";
        if (lower.Contains("bear")) return "Bearish";
        return "Neutral";
    }
}
