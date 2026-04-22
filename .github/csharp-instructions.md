---
description: "Use when writing or editing C# code in the Hound AI platform. Covers AF agent patterns, activity logging, DI conventions, and hound class structure."
applyTo: "src/**/*.cs"
---
# Hound AI — C# Conventions

## Agent Framework Patterns
- Hounds wrap `ChatClientAgent` from `Microsoft.Agents.AI` — keep as a private field
- Define tools via `AIFunctionFactory.Create(...)` passed to the agent constructor
- Run agents with `_agent.RunAsync(messages, options, cancellationToken)` — fresh session per call
- Parse responses with `JsonSerializer.Deserialize<T>(response.Text, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })`

## Activity Logging
- All hounds log activity via `IActivityLogger.LogActivityAsync()` **before and after** agent invocation
- Never use `Console.WriteLine` — use `IActivityLogger` for hound activity, `ILogger<T>` for infrastructure

## DI Registration
- Hounds registered as **singletons** in the pack's `Program.cs`
- Per-hound model configured via `builder.Configuration["Hounds:{Name}:Model"]`
- Use `IOllamaClientFactory` → cast to `OllamaClientFactory` to call `CreateChatClient(model)`
- Settings bound via `IOptions<T>` — config from `Config/*.json` and environment variables

## Hound Class Structure
- Const `HoundId` (kebab-case: `analysis-hound`) and `PackId` (kebab-case: `trading-pack`)
- Constructor: `IChatClient`, `IActivityLogger`, optional `ILoggerFactory?`, plus domain services
- Public async method returns a typed **record** (defined in shared models)
- `CancellationToken` on all public async methods

## Workflows
- Hound outputs feed into downstream hounds via typed records (e.g., `MarketAnalysis` → `TradingDecision`)
- Workflows are sequential chains orchestrated in `Workflows/` classes
- Confidence thresholds and symbols driven by `IOptions<TSettings>`
- Use `nameof` instead of string literals when referring to member names.
- Ensure that XML doc comments are created for any public APIs. When applicable, include `<example>` and `<code>` documentation in the comments.

## Project Setup and Structure

- Guide users through creating a new .NET project with the appropriate templates.
- Explain the purpose of each generated file and folder to build understanding of the project structure.
- Demonstrate how to organize code using feature folders or domain-driven design principles.
- Show proper separation of concerns with models, services, and data access layers.
- Explain the Program.cs and configuration system in ASP.NET Core 10 including environment-specific settings.

## Nullable Reference Types

- Declare variables non-nullable, and check for `null` at entry points.
- Always use `is null` or `is not null` instead of `== null` or `!= null`.
- Trust the C# null annotations and don't add null checks when the type system says a value cannot be null.

## Data Access Patterns

- Guide the implementation of a data access layer using Entity Framework Core.
- Explain different options (SQL Server, SQLite, In-Memory) for development and production.
- Demonstrate repository pattern implementation and when it's beneficial.
- Show how to implement database migrations and data seeding.
- Explain efficient query patterns to avoid common performance issues.

## Authentication and Authorization

- Guide users through implementing authentication using JWT Bearer tokens.
- Explain OAuth 2.0 and OpenID Connect concepts as they relate to ASP.NET Core.
- Show how to implement role-based and policy-based authorization.
- Demonstrate integration with Microsoft Entra ID (formerly Azure AD).
- Explain how to secure both controller-based and Minimal APIs consistently.

## Validation and Error Handling

- Guide the implementation of model validation using data annotations and FluentValidation.
- Explain the validation pipeline and how to customize validation responses.
- Demonstrate a global exception handling strategy using middleware.
- Show how to create consistent error responses across the API.
- Explain problem details (RFC 9457) implementation for standardized error responses.

## API Versioning and Documentation

- Guide users through implementing and explaining API versioning strategies.
- Demonstrate Swagger/OpenAPI implementation with proper documentation.
- Show how to document endpoints, parameters, responses, and authentication.
- Explain versioning in both controller-based and Minimal APIs.
- Guide users on creating meaningful API documentation that helps consumers.

## Logging and Monitoring

- Guide the implementation of structured logging using Serilog or other providers.
- Explain the logging levels and when to use each.
- Demonstrate integration with Application Insights for telemetry collection.
- Show how to implement custom telemetry and correlation IDs for request tracking.
- Explain how to monitor API performance, errors, and usage patterns.

## Testing

- Always include test cases for critical paths of the application.
- Guide users through creating unit tests.
- Do not emit "Act", "Arrange" or "Assert" comments.
- Copy existing style in nearby files for test method names and capitalization.
- Explain integration testing approaches for API endpoints.
- Demonstrate how to mock dependencies for effective testing.
- Show how to test authentication and authorization logic.
- Explain test-driven development principles as applied to API development.

## Performance Optimization

- Guide users on implementing caching strategies (in-memory, distributed, response caching).
- Explain asynchronous programming patterns and why they matter for API performance.
- Demonstrate pagination, filtering, and sorting for large data sets.
- Show how to implement compression and other performance optimizations.
- Explain how to measure and benchmark API performance.

## Deployment and DevOps

- Guide users through containerizing their API using .NET's built-in container support (`dotnet publish --os linux --arch x64 -p:PublishProfile=DefaultContainer`).
- Explain the differences between manual Dockerfile creation and .NET's container publishing features.
- Explain CI/CD pipelines for NET applications.
- Demonstrate deployment to Azure App Service, Azure Container Apps, or other hosting options.
- Show how to implement health checks and readiness probes.
- Explain environment-specific configurations for different deployment stages.
