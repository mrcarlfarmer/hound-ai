using Microsoft.AspNetCore.SignalR;
using Hound.Core.Models;

namespace Hound.Api.Hubs;

/// <summary>
/// SignalR hub for real-time activity streaming to the Angular dashboard.
/// </summary>
public class ActivityHub : Hub
{
    /// <summary>Subscribe client to receive activity events for a specific pack.</summary>
    public async Task SubscribeToPack(string packId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"pack-{packId}");
    }

    /// <summary>Unsubscribe client from a pack's activity events.</summary>
    public async Task UnsubscribeFromPack(string packId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"pack-{packId}");
    }

    /// <summary>
    /// Called by pack containers to broadcast activity events.
    /// Relays to all clients subscribed to the relevant pack.
    /// </summary>
    public async Task PublishActivity(ActivityLog activity)
    {
        await Clients.Group($"pack-{activity.PackId}").SendAsync("OnActivity", activity);
    }

    /// <summary>
    /// Broadcasts an order status update to clients subscribed to the trading pack.
    /// Triggered by the OrderWatcherService via the Trades API.
    /// </summary>
    public async Task PublishOrderUpdate(object orderUpdate)
    {
        await Clients.Group("pack-trading-pack").SendAsync("OnOrderUpdate", orderUpdate);
    }
}
