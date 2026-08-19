using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace MangaScrapper.Core.Hubs;

/// <summary>
/// SignalR Hub for real-time notifications related to manga and chapter updates.
/// </summary>
public sealed class MangaHub(ILogger<MangaHub> logger) : Hub
{
    /// <summary>
    /// Joins the group for a specific manga to receive localized chapter update notifications.
    /// </summary>
    public async Task JoinMangaGroup(string mangaId)
    {
        var groupName = GetMangaGroupName(mangaId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        logger.LogDebug("Connection {ConnectionId} joined group {GroupName}", Context.ConnectionId, groupName);
    }

    /// <summary>
    /// Leaves the group for a specific manga.
    /// </summary>
    public async Task LeaveMangaGroup(string mangaId)
    {
        var groupName = GetMangaGroupName(mangaId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        logger.LogDebug("Connection {ConnectionId} left group {GroupName}", Context.ConnectionId, groupName);
    }

    public static string GetMangaGroupName(string mangaId) => $"manga-{mangaId.ToLowerInvariant()}";
    public static string GetMangaGroupName(Guid mangaId) => $"manga-{mangaId.ToString().ToLowerInvariant()}";

    public override Task OnConnectedAsync()
    {
        logger.LogDebug("SignalR client connected: {ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogDebug("SignalR client disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }
}
