# Shared local LLM platform

## Status and scope

This is a saved implementation plan, not an implemented platform. Deployment,
platform repository creation and Hound AI integration require separate approval.

Build a containerized LLM platform independent of Hound AI, so multiple local
applications can share model routing, inference capacity and diagnostics. Use
Docker Compose on WSL2 initially, with Hound AI as the first integrated client.
Keep local Kubernetes as a future deployment option rather than maintaining two
deployments from the outset.

Use LiteLLM as the OpenAI-compatible gateway, Ollama as the initial inference
backend, and OpenRouter as an explicitly enabled cloud backend. Establish
measurements before changing inference engines or adding advanced KV caching.

## Confirmed decisions

- Shared platform for all compatible local applications, not a Hound-specific subsystem.
- Independent platform repository; only Hound client integration belongs here. Obtain the repository name and location before creating it.
- Docker Compose on WSL2 first; Kubernetes deployment is deferred.
- RTX 4070 Ti SUPER, nominally 16 GB VRAM. Verify usable VRAM and available system RAM before choosing runtime limits.
- Local by default. Cloud use requires an explicitly cloud-enabled application/model alias; local failures or congestion must not silently spill to OpenRouter.
- Metrics, correlated request traces and operational logs are in the baseline. Prompt/response content capture is disabled.

## Proposed architecture

Applications -> authenticated LiteLLM gateway -> local Ollama or explicitly selected OpenRouter route.

Independent Compose project:

| Component | Purpose |
| --- | --- |
| LiteLLM | Stable model aliases, OpenAI-compatible API, application keys, routing and request telemetry |
| PostgreSQL | LiteLLM virtual-key configuration and persistent usage/accounting metadata |
| Redis-compatible cache | Initially Redis, subject to version/license review; exact-response and embedding cache only for approved workloads |
| Ollama + model initialization | Local GPU inference and platform-owned model artifacts |
| Prometheus + Grafana | Metrics storage, dashboards and alerting |
| OpenTelemetry Collector + Tempo | Content-free trace collection and storage |
| Grafana Alloy + Loki | Selected container operational logs, with redaction and bounded storage |
| GPU/host collection | Supported WSL2 measurements; probe capabilities before choosing exporters |

OpenRouter is an external service, not a local inference container.

Keep database, cache, collectors and inference management endpoints private.
Publish gateway and Grafana on loopback initially. Use a shared external Docker
network for application containers and document separate Windows/WSL host URLs.
Container localhost does not refer to another service. Use one Docker engine/context
for both projects.

Use a maintained GPU exporter only if it supports the actual WSL2/GeForce
combination. Otherwise use available NVIDIA management measurements via a narrow
collector, or collect them on the host and document that exception. Do not promise
DCGM coverage or represent unsupported readings as zero.

## Routing, resource and privacy policy

- Give each application its own virtual key and alias allowlist; retain the master key for administration only.
- Start with capability aliases such as local-chat, local-reasoning, local-vision and local-embeddings. Map Hound's existing roles without accidentally substituting model capabilities or embedding dimensions.
- Cloud aliases have separate permissions, configurable spend limits and bounded retries. Validate the selected LiteLLM release's open-source capabilities; do not depend on enterprise-only features.
- Keep provider credentials in local secret configuration, never source or client applications. Check provider retention/privacy policies before enabling cloud aliases.
- Configure timeouts, bounded queues and conservative concurrency for one 16 GB GPU. Start with one active heavyweight generation; adjust from measurements. Streaming cancellations must release backend work.
- Platform policy owns model residency. Applications must not unload models shared by other clients.
- Pin container versions or digests after compatibility, licensing and dependency review. Bound CPU, RAM and disk consumption of observability services as well as inference.
- Disable content capture at LiteLLM, SDK instrumentation, Collector, logs and persistence settings; verify with sentinel content rather than assuming defaults are safe. Keep keys, prompts, tool arguments/results and user identifiers out of metric labels.
- Use bounded labels such as application, alias, provider and outcome. Put request/run/node identifiers in traces, not Prometheus labels.
- Proposed initial retention: seven days for metrics/logs and three days for traces, configurable within explicit storage caps.

## Caching strategy

These mechanisms are not interchangeable:

1. Response caching returns a previous answer without inference. LiteLLM plus Redis can implement exact-request caching. Default off for chat, trading and tool-using flows; enable only per approved deterministic workload with explicit TTL, application isolation, model/version and relevant generation settings in the cache identity. Embedding caching is an early candidate, keyed by exact input and embedding model/version.
2. KV/prefix caching reuses internal attention state to avoid repeated prompt prefill, while still generating a new answer. It belongs to the inference engine. It is model/runtime/tokenization-specific, not a generic Redis feature and not generally portable between local and hosted models.
3. Ollama runtime KV settings and model residency can be tuned without replacing the server. Quantized KV storage can reduce VRAM pressure, but is not evidence of reusable cross-request prefixes or persistent cache storage.
4. Benchmark a separate vLLM profile with native automatic prefix caching after the baseline works. Verify GPU, quantization, model architecture and artifact compatibility; Ollama artifacts are not automatically reusable by vLLM.
5. Evaluate LMCache only if native prefix caching leaves a measured prefill bottleneck and available RAM/SSD can support offload. It needs a supported inference integration; do not place it in front of Ollama and assume it works.

Avoid semantic response caching initially: near-matching prompts can have different
market data, intent or tool state. Do not cache side-effecting operations or treat
an old tool response as current state.

## Observability and diagnosis

Dashboard slices: platform overview, per-application/provider/alias traffic, local
inference, cache behavior and failures.

Collect where supported:

- Request volume, in-flight work, queue time/depth, retries, timeouts, cancellations, errors and routing decisions.
- End-to-end latency and time to first token, including p50/p95/p99. Distinguish client-observed TTFT from gateway/provider timings; do not label first-byte latency as first-token latency.
- Input/output token counts, successful generation throughput and per-request decode rate when actual counts/timing are available. Separate cache hits from generated requests.
- Model cold-load time, resident models, VRAM, GPU utilization, power/temperature where supported, CPU/RAM and storage pressure.
- Response/embedding hit rate and TTL/evictions; KV-prefix hit rate, KV occupancy and prefill/decode/queue timings only when exposed by the backend.
- Cloud reported/estimated cost with provenance, budget consumption and denials. Do not equate local token price with electricity/hardware cost.
- Service/exporter health, missing scrapes and telemetry pipeline failures.

Correlate application spans -> gateway spans -> supported backend spans through
validated trace propagation. For backends without trace support, correlate gateway
timing and backend metrics/logs without inventing internal spans. Record Hound
pack/run/node context on application traces while preserving existing RavenDB
activity logs and dashboard streaming.

Create a measurement capability matrix. Label unavailable or estimated signals:
Ollama's OpenAI-compatible interface may not expose native timings or KV
utilization, and WSL2 GPU telemetry is not equivalent to native Linux. Full
per-layer diagnosis is a goal, not a promise the proxy alone can satisfy.

## Current Hound AI integration surfaces

- `docker-compose.yml` owns Ollama and ollama-init alongside the application. The init script also depends on Hound activity reporting; shared initialization must not depend on Hound availability.
- `src\Hound.Core\LlmClient\OllamaClientFactory.cs` uses the OpenAI SDK but hardcodes the API credential to `"ollama"`. Add a provider-neutral configured client path while preserving a direct-Ollama option and existing defaults.
- `src\Hound.Core\LlmClient\StreamingChatClient.cs` preserves dashboard streaming and sampling defaults. Retain these semantics, including cancellation and tools.
- Trading and Grocery `Program.cs` register keyed chat clients. Grocery separately constructs its embedding client with the same hardcoded credential; migrate both surfaces.
- `src\Hound.Trading\Graph\OllamaResettableExecutor.cs` unloads two hardcoded models directly. Shared-platform mode must not execute global unload requests; retain direct-mode behavior until intentionally migrated.
- `src\Hound.Api\Services\HealthCheckService.cs` probes Ollama `/api/tags`, not a provider-neutral endpoint. Separate gateway readiness from optional backend diagnostics; do not infer loaded residency from the model catalog.
- `src\Hound.Eval\EvalRunner.cs` creates clients and needs gateway/auth/model selection checked without expanding evaluation scope.
- Compose environment overrides and dependencies must support an external platform with clear readiness failures, not start a second private Ollama instance.
- Preserve existing Ollama data. Inventory volumes and document a backed-up reuse/migration path; a different Compose project does not automatically find the same volume.

## Implementation todos

1. Qualifying the host and component versions: verify Docker/WSL GPU access, usable VRAM, RAM/disk, exporter support, release features and licenses. Capture a direct-Ollama cold/warm baseline without changing running workloads. Obtain platform repository name/location.
2. Building the independent platform: create Compose, pinned images, configuration/secrets templates, network/volumes, independent model initialization, per-app keys, explicit local/cloud aliases, resource limits, health checks and startup/backup runbooks.
3. Adding diagnostic telemetry: provision metrics, traces, operational logs, retention/storage caps, capability matrix, dashboards and alerts. Validate content redaction and correlation.
4. Integrating Hound AI: add provider-neutral endpoint/key settings, migrate chat/embeddings, preserve role models and streaming, adjust shared lifecycle and health reporting, and supply external-platform Compose integration. Retain direct mode and rollback.
5. Validating workloads and safe caching: check compatibility, isolation, failures and concurrency; enable only proven-safe exact/embedding caches. Compare direct inference, uncached gateway, cache misses and hits.
6. Experimenting with inference KV caching: benchmark compatible vLLM native prefix caching, then LMCache only if justified. Record adopt/defer decisions and exact model/runtime configurations.
7. Documenting operation and future Kubernetes: finish generic application onboarding, backup/restore, secret rotation, measured sizing and troubleshooting. Document Kubernetes prerequisites, GPU device management and persistent storage without implementing manifests yet.

Dependencies: 2 requires 1; 3 and 4 require 2; 5 requires 3 and 4; 6 requires 5;
7 requires 5 and 6. A documented defer decision completes 6.

## Acceptance and benchmark criteria

- Platform starts/stops independently of Hound AI; a generic second OpenAI-compatible client works without Hound running.
- Local keys cannot select cloud aliases. Provider failure, busy GPU and queue overflow never trigger unapproved cloud requests. Cloud tests require explicit authorization and a configured test budget.
- Chat, tools, structured outputs, streaming, vision and embeddings work for configured aliases; unsupported capabilities fail explicitly. Verify cancellation, timeouts and backend/auth failures.
- Preserve existing Hound unit tests and relevant offline eval validation. Run Angular checks only if UI contracts change. Add focused settings, credential, direct/shared lifecycle and health tests; follow repository test instructions and skills.
- Sentinel prompt/response/tool/credential values do not appear in logs, traces, metrics or persisted spend metadata. Applications cannot read each other's cached results.
- Compare fixed representative cold/warm, repeated-prefix, embedding and mixed-client workloads with response caching disabled during inference comparisons. Record lengths, model/version, quantization, context, concurrency, TTFT, latency, tokens/second, memory and quality checks.
- Proposed gateway warm overhead target: median <= 50 ms and p95 <= 100 ms above direct inference under the same workload. Use at least 100 comparable warm requests; report distributions and regressions.
- Cache-hit checks prove no upstream generation occurred and changed inputs/model/options miss correctly. Trading and side-effect-sensitive workloads remain uncached.
- Adopt an inference/cache profile only with compatible outputs and repeatable >= 20% median TTFT improvement on the repeated-prefix workload, without > 10% p95 end-to-end regression on mixed traffic or exceeding resource budgets. Otherwise retain Ollama and document the result.
- Unsupported backend/exporter signals appear as unavailable. Missing telemetry and collection failures are visible.
- Restart and backup/restore preserve configuration/model data; rollback restores direct Hound inference without deleting shared volumes.

## Sources checked

- [LiteLLM OpenRouter integration](https://docs.litellm.ai/docs/providers/openrouter)
- [LiteLLM response caching](https://docs.litellm.ai/docs/proxy/caching)
- [LiteLLM virtual keys and PostgreSQL requirements](https://docs.litellm.ai/docs/proxy/virtual_keys)
- [LiteLLM Prometheus integration](https://docs.litellm.ai/docs/proxy/prometheus)
- [LiteLLM OpenTelemetry and content-capture caveats](https://docs.litellm.ai/docs/observability/opentelemetry_integration)
- [Ollama runtime configuration](https://docs.ollama.com/faq)
- [vLLM native prefix caching](https://docs.vllm.ai/en/stable/features/automatic_prefix_caching/)
- [LMCache architecture and storage tiers](https://docs.lmcache.ai/)

Documentation is rolling. Verify behavior against pinned versions during
implementation. Detailed WSL2 GPU monitoring support, selected-version licensing
and feature availability remain preflight checks.
