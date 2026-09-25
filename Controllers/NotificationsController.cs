using System.Security.Claims;
using Kue.Api.Data;
using Kue.Api.Dtos.Common;
using Kue.Api.Dtos.Media;
using Kue.Api.Dtos.Notifications;
using Kue.Api.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        AppDbContext context,
        IHubContext<NotificationHub> hubContext,
        ILogger<NotificationsController> logger)
    {
        _context = context;
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <summary>
    /// Get paginated notifications for the current authenticated user.
    /// </summary>
    [HttpGet("me/notifications")]
    [HttpGet("notifications")]
    [ProducesResponseType(typeof(PagedResponseDto<NotificationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] bool unreadOnly = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 20;

        var query = _context.Notifications
            .AsNoTracking()
            .Include(n => n.Actor)
            .Where(n => n.UserId == currentUserId.Value);

        if (unreadOnly)
        {
            query = query.Where(n => !n.IsRead);
        }

        var totalItems = await query.CountAsync(ct);

        var notifications = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new NotificationDto
            {
                Id = n.Id,
                UserId = n.UserId,
                ActorId = n.ActorId,
                ActorUsername = n.Actor.Username,
                Type = n.Type,
                Title = n.Title,
                Message = n.Message,
                ReferenceId = n.ReferenceId,
                ReferenceType = n.ReferenceType,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt,
                ReadAt = n.ReadAt
            })
            .ToListAsync(ct);

        return Ok(new PagedResponseDto<NotificationDto>
        {
            Items = notifications,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling((double)totalItems / pageSize)
        });
    }

    /// <summary>
    /// Get the count of unread notifications for the current user.
    /// </summary>
    [HttpGet("me/notifications/unread-count")]
    [HttpGet("notifications/unread-count")]
    [ProducesResponseType(typeof(UnreadNotificationCountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetUnreadCount(CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var count = await _context.Notifications
            .CountAsync(n => n.UserId == currentUserId.Value && !n.IsRead, ct);

        return Ok(new UnreadNotificationCountDto
        {
            UnreadCount = count
        });
    }

    /// <summary>
    /// Mark a specific notification as read.
    /// </summary>
    [HttpPut("me/notifications/{id:int}/read")]
    [HttpPut("notifications/{id:int}/read")]
    [ProducesResponseType(typeof(NotificationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MarkAsRead(int id, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var notification = await _context.Notifications
            .Include(n => n.Actor)
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == currentUserId.Value, ct);

        if (notification is null)
        {
            return NotFound(new MessageResponseDto($"Notification with ID {id} not found."));
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);

            var unreadCount = await _context.Notifications
                .CountAsync(n => n.UserId == currentUserId.Value && !n.IsRead, ct);

            await _hubContext.Clients.User(currentUserId.Value.ToString())
                .SendAsync("UpdateUnreadCount", unreadCount, ct);
        }

        return Ok(new NotificationDto
        {
            Id = notification.Id,
            UserId = notification.UserId,
            ActorId = notification.ActorId,
            ActorUsername = notification.Actor.Username,
            Type = notification.Type,
            Title = notification.Title,
            Message = notification.Message,
            ReferenceId = notification.ReferenceId,
            ReferenceType = notification.ReferenceType,
            IsRead = notification.IsRead,
            CreatedAt = notification.CreatedAt,
            ReadAt = notification.ReadAt
        });
    }

    /// <summary>
    /// Mark all notifications as read for the current user.
    /// </summary>
    [HttpPut("me/notifications/read-all")]
    [HttpPut("notifications/read-all")]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var unreadNotifications = await _context.Notifications
            .Where(n => n.UserId == currentUserId.Value && !n.IsRead)
            .ToListAsync(ct);

        if (unreadNotifications.Count > 0)
        {
            var now = DateTime.UtcNow;
            foreach (var n in unreadNotifications)
            {
                n.IsRead = true;
                n.ReadAt = now;
            }

            await _context.SaveChangesAsync(ct);
        }

        await _hubContext.Clients.User(currentUserId.Value.ToString())
            .SendAsync("UpdateUnreadCount", 0, ct);

        return Ok(new MessageResponseDto($"{unreadNotifications.Count} notifications marked as read."));
    }

    /// <summary>
    /// Delete a specific notification.
    /// </summary>
    [HttpDelete("me/notifications/{id:int}")]
    [HttpDelete("notifications/{id:int}")]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteNotification(int id, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var notification = await _context.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == currentUserId.Value, ct);

        if (notification is null)
        {
            return NotFound(new MessageResponseDto($"Notification with ID {id} not found."));
        }

        var wasUnread = !notification.IsRead;
        _context.Notifications.Remove(notification);
        await _context.SaveChangesAsync(ct);

        if (wasUnread)
        {
            var unreadCount = await _context.Notifications
                .CountAsync(n => n.UserId == currentUserId.Value && !n.IsRead, ct);

            await _hubContext.Clients.User(currentUserId.Value.ToString())
                .SendAsync("UpdateUnreadCount", unreadCount, ct);
        }

        return Ok(new MessageResponseDto("Notification deleted successfully."));
    }

    /// <summary>
    /// Delete all notifications for the current user.
    /// </summary>
    [HttpDelete("me/notifications")]
    [HttpDelete("notifications")]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ClearAllNotifications(CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var notifications = await _context.Notifications
            .Where(n => n.UserId == currentUserId.Value)
            .ToListAsync(ct);

        if (notifications.Count > 0)
        {
            _context.Notifications.RemoveRange(notifications);
            await _context.SaveChangesAsync(ct);
        }

        await _hubContext.Clients.User(currentUserId.Value.ToString())
            .SendAsync("UpdateUnreadCount", 0, ct);

        return Ok(new MessageResponseDto("All notifications cleared."));
    }

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? id : null;
    }
}
