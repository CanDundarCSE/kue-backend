using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Kue.Api.Hubs;

/// <summary>
/// SignalR Hub for real-time notifications and unread badge updates.
/// Clients connect to /hubs/notifications with their JWT bearer token.
/// </summary>
[Authorize]
public class NotificationHub : Hub
{
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(ILogger<NotificationHub> logger)
    {
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        _logger.LogInformation("SignalR: User {UserId} connected to NotificationHub (ConnectionId: {ConnectionId})", userId, Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.UserIdentifier;
        if (exception != null)
        {
            _logger.LogWarning(exception, "SignalR: User {UserId} disconnected with error (ConnectionId: {ConnectionId})", userId, Context.ConnectionId);
        }
        else
        {
            _logger.LogInformation("SignalR: User {UserId} disconnected from NotificationHub (ConnectionId: {ConnectionId})", userId, Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }
}
