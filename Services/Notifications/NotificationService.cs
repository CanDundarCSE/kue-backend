using Kue.Api.Data;
using Kue.Api.Dtos.Notifications;
using Kue.Api.Entities;
using Kue.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Services.Notifications;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _context;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        AppDbContext context,
        IHubContext<NotificationHub> hubContext,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task CreateNotificationAsync(
        int userId,
        int actorId,
        string type,
        string title,
        string message,
        int? referenceId = null,
        string? referenceType = null,
        CancellationToken ct = default)
    {
        if (userId == actorId) return;

        try
        {
            var notification = new Notification
            {
                UserId = userId,
                ActorId = actorId,
                Type = type,
                Title = title,
                Message = message,
                ReferenceId = referenceId,
                ReferenceType = referenceType,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync(ct);

            // Fetch actor username for real-time payload
            var actorUsername = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == actorId)
                .Select(u => u.Username)
                .FirstOrDefaultAsync(ct) ?? "User";

            var dto = new NotificationDto
            {
                Id = notification.Id,
                UserId = notification.UserId,
                ActorId = notification.ActorId,
                ActorUsername = actorUsername,
                Type = notification.Type,
                Title = notification.Title,
                Message = notification.Message,
                ReferenceId = notification.ReferenceId,
                ReferenceType = notification.ReferenceType,
                IsRead = notification.IsRead,
                CreatedAt = notification.CreatedAt,
                ReadAt = notification.ReadAt
            };

            var unreadCount = await _context.Notifications
                .CountAsync(n => n.UserId == userId && !n.IsRead, ct);

            // Push real-time event to the specific recipient via SignalR
            await _hubContext.Clients.User(userId.ToString())
                .SendAsync("ReceiveNotification", dto, ct);

            await _hubContext.Clients.User(userId.ToString())
                .SendAsync("UpdateUnreadCount", unreadCount, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create or broadcast notification of type {Type} for user {UserId}", type, userId);
        }
    }
}
