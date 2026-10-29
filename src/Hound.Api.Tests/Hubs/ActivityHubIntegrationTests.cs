using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Hound.Api.Controllers;
using Hound.Api.Hubs;
using Hound.Api.Repositories;
using Hound.Api.Services;
using Hound.Core.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Hound.Api.Tests.Hubs;

[TestClass]
public sealed class ActivityHubIntegrationTests
{
    [TestMethod]
    public async Task Api_HttpAndSignalR_PreserveCamelCaseContracts()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var ct = timeout.Token;
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSignalR();
        builder.Services.AddControllers().AddApplicationPart(typeof(HealthController).Assembly);
        var health = new Mock<IHealthCheckService>();
        health.Setup(service => service.CheckAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HealthReport { Status = HealthStatus.Healthy });
        var packs = new Mock<IPackRepository>();
        packs.Setup(repo => repo.GetAllPacksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Pack { Id = "upgrade-smoke", Name = "Smoke Pack" }]);
        builder.Services.AddSingleton(health.Object);
        builder.Services.AddSingleton(packs.Object);
        await using var app = builder.Build();
        app.MapHub<ActivityHub>("/hubs/activity");
        app.MapControllers();
        await app.StartAsync(ct);

        try
        {
            var address = app.Urls.Single();
            using var http = new HttpClient { BaseAddress = new Uri(address) };
            using var healthJson = JsonDocument.Parse(await http.GetStringAsync("/api/health", ct));
            Assert.AreEqual("Healthy", healthJson.RootElement.GetProperty("status").GetString());
            using var packsJson = JsonDocument.Parse(await http.GetStringAsync("/api/packs", ct));
            Assert.AreEqual("upgrade-smoke", packsJson.RootElement[0].GetProperty("id").GetString());
            using var response = await http.PostAsync("/hubs/activity/negotiate?negotiateVersion=1", null, ct);
            response.EnsureSuccessStatusCode();
            using var negotiate = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var token = negotiate.RootElement.GetProperty("connectionToken").GetString();
            Assert.IsNotNull(token);
            using var socket = new ClientWebSocket();
            var uri = new UriBuilder(address)
            {
                Scheme = "ws",
                Path = "/hubs/activity",
                Query = $"id={Uri.EscapeDataString(token)}",
            }.Uri;
            await socket.ConnectAsync(uri, ct);
            await SendAsync(socket, """{"protocol":"json","version":1}""", ct);
            var pending = new Queue<JsonElement>();
            using var stream = new MemoryStream();
            var handshake = await ReceiveAsync(socket, pending, stream, ct);
            Assert.AreEqual(JsonValueKind.Object, handshake.ValueKind);
            Assert.IsFalse(handshake.TryGetProperty("error", out _));

            await SendAsync(socket,
                """{"type":1,"invocationId":"subscribe","target":"SubscribeToPack","arguments":["upgrade-smoke"]}""", ct);
            JsonElement message;
            do
            {
                message = await ReceiveAsync(socket, pending, stream, ct);
            } while (!message.TryGetProperty("invocationId", out var id) || id.GetString() != "subscribe");
            Assert.AreEqual(3, message.GetProperty("type").GetInt32());
            Assert.IsFalse(message.TryGetProperty("error", out _));

            var hub = app.Services.GetRequiredService<Microsoft.AspNetCore.SignalR.IHubContext<ActivityHub>>();
            await hub.Clients.Group("pack-upgrade-smoke").SendCoreAsync("OnActivity",
                [new ActivityLog { PackId = "upgrade-smoke", HoundId = "smoke", Message = "upgrade event" }], ct);
            do
            {
                message = await ReceiveAsync(socket, pending, stream, ct);
            } while (message.GetProperty("type").GetInt32() != 1);
            Assert.AreEqual("OnActivity", message.GetProperty("target").GetString());
            var activity = message.GetProperty("arguments")[0];
            Assert.AreEqual("upgrade-smoke", activity.GetProperty("packId").GetString());
            Assert.AreEqual("upgrade event", activity.GetProperty("message").GetString());
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }

    private static Task SendAsync(ClientWebSocket socket, string json, CancellationToken ct) =>
        socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json + "\u001e")),
            WebSocketMessageType.Text, true, ct);

    private static async Task<JsonElement> ReceiveAsync(
        ClientWebSocket socket, Queue<JsonElement> pending, MemoryStream stream, CancellationToken ct)
    {
        var buffer = new byte[4096];
        while (pending.Count == 0)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            Assert.AreEqual(WebSocketMessageType.Text, result.MessageType);
            for (var i = 0; i < result.Count; i++)
            {
                if (buffer[i] != 0x1e)
                {
                    stream.WriteByte(buffer[i]);
                    continue;
                }

                using var document = JsonDocument.Parse(stream.ToArray());
                pending.Enqueue(document.RootElement.Clone());
                stream.SetLength(0);
            }
        }

        return pending.Dequeue();
    }
}
