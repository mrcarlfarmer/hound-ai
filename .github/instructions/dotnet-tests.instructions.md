---
description: "Use when writing or modifying .NET unit tests. Covers MSTest + Moq patterns, test structure, and conventions for hound, controller, hub, and service tests."
applyTo: "src/**/*.Tests/**"
---
# .NET Test Conventions

## Framework
- **MSTest** (`[TestClass]`, `[TestMethod]`, `[TestInitialize]`) + **Moq**
- Parallelization enabled: `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]` in `MSTestSettings.cs`
- File-scoped namespaces matching project: `namespace Hound.Api.Tests.Controllers;`

## Structure
- Test class per production class: `AnalysisHoundTests`, `PacksControllerTests`
- `[TestInitialize] Setup()` creates mocks and SUT
- Field naming: `_mockLogger`, `_mockRepo`, `_controller`, `_service`

```csharp
[TestClass]
public class MyControllerTests
{
    private Mock<IPackRepository> _mockRepo = null!;
    private MyController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockRepo = new Mock<IPackRepository>();
        _controller = new MyController(_mockRepo.Object);
    }
}
```

## Common Mocks
- `Mock<IActivityLogger>` — verify `LogActivityAsync` calls
- `Mock<IPackRepository>`, `Mock<ITunerExperimentRepository>` — stub return values
- `Mock<IGroupManager>`, `Mock<IHubCallerClients>` — for SignalR hub tests
- `IOptions<T>` via `Options.Create(new TSettings { ... })`

## Assertions
- `Assert.IsNotNull`, `Assert.AreEqual`, `Assert.IsInstanceOfType<T>`
- Cast `ActionResult<T>.Result` to `OkObjectResult`, `NotFoundResult`, `ConflictObjectResult`
- Verify mock calls with `_mock.Verify(..., Times.Once)`

## Patterns
- All async tests: `public async Task MethodName_Scenario_ExpectedResult()`
- `CancellationToken` passed as `default` in test calls
- `[Ignore("reason")]` for tests requiring live credentials or network
- Helper methods: `private static IOptions<T> CreateTestOptions()` for settings
