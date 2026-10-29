using Alpaca.Markets;
using Hound.Api.Hubs;
using Hound.Api.Services;
using Hound.Core.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Raven.Client.Documents;
using Raven.Embedded;

namespace Hound.Api.Tests.Services;

[TestClass]
[DoNotParallelize]
public sealed class AlpacaSyncServiceTests
{
    [TestMethod]
    [DataRow("AAPL", OrderSide.Buy, 1, 0)]
    [DataRow(null, OrderSide.Buy, 0, 1)]
    [DataRow("AAPL", null, 0, 1)]
    public async Task SyncAsync_ExternalOrder_ImportsOnlyOrdersWithSymbolAndSide(
        string? symbol, OrderSide? side, int imported, int errors)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hound-sync-tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var server = EmbeddedServer.Instance;
            server.StartServer(new ServerOptions { DataDirectory = directory, FrameworkVersion = "10.0.0+" });
            using var store = server.GetDocumentStore("hound-trading-pack");
            var order = new Mock<IOrder>();
            order.SetupGet(value => value.OrderId).Returns(Guid.NewGuid());
            order.SetupGet(value => value.Symbol).Returns(symbol);
            order.SetupGet(value => value.OrderSide).Returns(side);
            order.SetupGet(value => value.OrderStatus).Returns(OrderStatus.New);
            var alpaca = new Mock<IAlpacaPortfolioService>();
            alpaca.Setup(service => service.ListOrdersAsync(
                    OrderStatusFilter.All, It.IsAny<DateTime?>(), 500, It.IsAny<CancellationToken>()))
                .ReturnsAsync([order.Object]);
            var hub = new Mock<IHubContext<ActivityHub>>();
            var clients = new Mock<IHubClients>();
            var client = new Mock<IClientProxy>();
            client.Setup(proxy => proxy.SendCoreAsync(
                    It.IsAny<string>(), It.IsAny<object?[]>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            clients.Setup(value => value.Group(It.IsAny<string>())).Returns(client.Object);
            hub.SetupGet(value => value.Clients).Returns(clients.Object);
            var service = new AlpacaSyncService(store, alpaca.Object, hub.Object, NullLogger<AlpacaSyncService>.Instance);

            var result = await service.SyncAsync();

            Assert.AreEqual(imported, result.Imported);
            Assert.AreEqual(errors, result.Errors);
            Assert.AreEqual(imported, result.ChangedTradeIds.Count);
            if (imported > 0)
            {
                using var session = store.OpenAsyncSession("hound-trading-pack");
                var trade = await session.LoadAsync<TradeDocument>(result.ChangedTradeIds[0]);
                Assert.AreEqual(symbol, trade.Symbol);
                Assert.AreEqual(side.ToString(), trade.Action);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
