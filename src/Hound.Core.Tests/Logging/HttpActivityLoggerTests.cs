using Hound.Core.Logging;
using Hound.Core.Models;
using Moq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Hound.Core.Tests.Logging;

[TestClass]
public sealed class HttpActivityLoggerTests
{
    private const string BaseUrl = "http://hound-api:5000";

    /// <summary>
    /// Creates a mock <see cref="IHttpClientFactory"/> that returns an <see cref="HttpClient"/>
    /// backed by the provided <see cref="HttpMessageHandler"/>.
    /// </summary>
    private static Mock<IHttpClientFactory> CreateFactory(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return factory;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // LogActivityAsync
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LogActivityAsync_PostsToCorrectEndpoint()
    {
        Uri? capturedUri = null;
        var handler = new TestHttpMessageHandler(req =>
        {
            capturedUri = req.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var logger = new HttpActivityLogger(CreateFactory(handler).Object, BaseUrl);
        await logger.LogActivityAsync(new ActivityLog { PackId = "trading-pack" });

        Assert.AreEqual($"{BaseUrl}/api/activity", capturedUri?.ToString());
    }

    [TestMethod]
    public async Task LogActivityAsync_UsesPostMethod()
    {
        HttpMethod? capturedMethod = null;
        var handler = new TestHttpMessageHandler(req =>
        {
            capturedMethod = req.Method;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var logger = new HttpActivityLogger(CreateFactory(handler).Object, BaseUrl);
        await logger.LogActivityAsync(new ActivityLog { PackId = "trading-pack" });

        Assert.AreEqual(HttpMethod.Post, capturedMethod);
    }

    [TestMethod]
    public async Task LogActivityAsync_SerializesActivityInBody()
    {
        ActivityLog? deserialized = null;
        var handler = new TestHttpMessageHandler(async req =>
        {
            deserialized = await req.Content!.ReadFromJsonAsync<ActivityLog>();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var activity = new ActivityLog
        {
            PackId = "trading-pack",
            HoundId = "analysis-hound",
            HoundName = "AnalysisHound",
            Message = "Bullish signal",
            Severity = ActivitySeverity.Info,
        };

        var logger = new HttpActivityLogger(CreateFactory(handler).Object, BaseUrl);
        await logger.LogActivityAsync(activity);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(activity.PackId, deserialized.PackId);
        Assert.AreEqual(activity.HoundId, deserialized.HoundId);
        Assert.AreEqual(activity.Message, deserialized.Message);
    }

    [TestMethod]
    public async Task LogActivityAsync_ThrowsOnErrorStatusCode()
    {
        var handler = new TestHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var logger = new HttpActivityLogger(CreateFactory(handler).Object, BaseUrl);

        await Assert.ThrowsExceptionAsync<HttpRequestException>(
            () => logger.LogActivityAsync(new ActivityLog { PackId = "trading-pack" }));
    }

    [TestMethod]
    public async Task LogActivityAsync_TrimsTrailingSlashFromBaseUrl()
    {
        Uri? capturedUri = null;
        var handler = new TestHttpMessageHandler(req =>
        {
            capturedUri = req.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var logger = new HttpActivityLogger(CreateFactory(handler).Object, BaseUrl + "/");
        await logger.LogActivityAsync(new ActivityLog { PackId = "trading-pack" });

        Assert.AreEqual($"{BaseUrl}/api/activity", capturedUri?.ToString());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // GetActivitiesAsync
    // ──────────────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task GetActivitiesAsync_UsesGetMethod()
    {
        HttpMethod? capturedMethod = null;
        var handler = new TestHttpMessageHandler(req =>
        {
            capturedMethod = req.Method;
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json");
            return response;
        });

        var logger = new HttpActivityLogger(CreateFactory(handler).Object, BaseUrl);
        await logger.GetActivitiesAsync();

        Assert.AreEqual(HttpMethod.Get, capturedMethod);
    }

    [TestMethod]
    public async Task GetActivitiesAsync_IncludesPackIdQueryParam()
    {
        Uri? capturedUri = null;
        var handler = new TestHttpMessageHandler(req =>
        {
            capturedUri = req.RequestUri;
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent("[]", System.Text.Encoding.UTF8, "application/json");
            return response;
        });

        var logger = new HttpActivityLogger(CreateFactory(handler).Object, BaseUrl);
        await logger.GetActivitiesAsync(packId: "trading-pack");

        Assert.IsNotNull(capturedUri);
        StringAssert.Contains(capturedUri.Query, "pack=trading-pack");
    }

    [TestMethod]
    public async Task GetActivitiesAsync_ReturnsDeserializedResults()
    {
        var expected = new List<ActivityLog>
        {
            new() { PackId = "trading-pack", HoundId = "risk-hound", Message = "Risk check passed" }
        };

        var handler = new TestHttpMessageHandler(_ =>
        {
            var json = JsonSerializer.Serialize(expected);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            return response;
        });

        var logger = new HttpActivityLogger(CreateFactory(handler).Object, BaseUrl);
        var results = await logger.GetActivitiesAsync();

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("trading-pack", results[0].PackId);
        Assert.AreEqual("Risk check passed", results[0].Message);
    }

    [TestMethod]
    public async Task GetActivitiesAsync_ReturnsEmpty_WhenApiReturnsNull()
    {
        var handler = new TestHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json");
            return response;
        });

        var logger = new HttpActivityLogger(CreateFactory(handler).Object, BaseUrl);
        var results = await logger.GetActivitiesAsync();

        Assert.AreEqual(0, results.Count);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helper: synchronous TestHttpMessageHandler
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
            : this(req => Task.FromResult(handler(req))) { }

        public TestHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
            => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request);
    }
}
