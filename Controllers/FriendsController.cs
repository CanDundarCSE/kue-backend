using System.Security.Claims;
using Kue.Api.Data;
using Kue.Api.Dtos.Common;
using Kue.Api.Dtos.Friends;
using Kue.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/friends")]
[Produces("application/json")]
[Authorize]
public class FriendsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<FriendsController> _logger;

    public FriendsController(AppDbContext context, ILogger<FriendsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Send a friend request to a user by username. If the other user already sent a pending request, auto-accepts into mutual friends.
    /// </summary>
    [HttpPost("request/{username}")]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> SendFriendRequest(string username, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var normalizedUsername = username.Trim().ToLower();
        var targetUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == normalizedUsername, ct);

        if (targetUser is null)
        {
            return NotFound(new MessageResponseDto($"User '{username}' was not found."));
        }

        if (targetUser.Id == currentUserId.Value)
        {
            return BadRequest(new MessageResponseDto("You cannot send a friend request to yourself."));
        }

        // Check if a relationship already exists in either direction
        var existing = await _context.Friendships
            .FirstOrDefaultAsync(f =>
                (f.RequesterId == currentUserId.Value && f.AddresseeId == targetUser.Id) ||
                (f.RequesterId == targetUser.Id && f.AddresseeId == currentUserId.Value), ct);

        if (existing != null)
        {
            if (existing.Status == "accepted")
            {
                return BadRequest(new MessageResponseDto($"You are already friends with '{targetUser.Username}'."));
            }

            if (existing.RequesterId == currentUserId.Value && existing.Status == "pending")
            {
                return BadRequest(new MessageResponseDto("Friend request has already been sent and is pending."));
            }

            // If the other user sent us a request, auto-accept!
            if (existing.RequesterId == targetUser.Id && existing.Status == "pending")
            {
                existing.Status = "accepted";
                existing.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
                return Ok(new MessageResponseDto($"Friend request from '{targetUser.Username}' automatically accepted. You are now mutual friends!"));
            }

            // If previously declined or cancelled, re-open as pending
            existing.RequesterId = currentUserId.Value;
            existing.AddresseeId = targetUser.Id;
            existing.Status = "pending";
            existing.CreatedAt = DateTime.UtcNow;
            existing.UpdatedAt = null;
            await _context.SaveChangesAsync(ct);

            return StatusCode(StatusCodes.Status201Created, new MessageResponseDto($"Friend request sent to '{targetUser.Username}'."));
        }

        var friendship = new Friendship
        {
            RequesterId = currentUserId.Value,
            AddresseeId = targetUser.Id,
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };

        _context.Friendships.Add(friendship);
        await _context.SaveChangesAsync(ct);

        return StatusCode(StatusCodes.Status201Created, new MessageResponseDto($"Friend request sent to '{targetUser.Username}'."));
    }

    /// <summary>
    /// Accept an incoming friend request by request ID.
    /// </summary>
    [HttpPost("requests/{requestId:int}/accept")]
    [ProducesResponseType(typeof(FriendDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AcceptFriendRequest(int requestId, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var request = await _context.Friendships
            .Include(f => f.Requester)
            .FirstOrDefaultAsync(f => f.Id == requestId, ct);

        if (request is null)
        {
            return NotFound(new MessageResponseDto($"Friend request with ID {requestId} not found."));
        }

        if (request.AddresseeId != currentUserId.Value)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponseDto("You can only accept friend requests sent to you."));
        }

        if (request.Status == "accepted")
        {
            return BadRequest(new MessageResponseDto("This friend request is already accepted."));
        }

        request.Status = "accepted";
        request.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        var dto = new FriendDto
        {
            FriendshipId = request.Id,
            UserId = request.Requester.Id,
            Username = request.Requester.Username,
            Bio = request.Requester.Bio,
            FriendsSince = request.UpdatedAt ?? request.CreatedAt
        };

        return Ok(dto);
    }

    /// <summary>
    /// Decline an incoming friend request by request ID.
    /// </summary>
    [HttpPost("requests/{requestId:int}/decline")]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeclineFriendRequest(int requestId, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var request = await _context.Friendships.FirstOrDefaultAsync(f => f.Id == requestId, ct);
        if (request is null)
        {
            return NotFound(new MessageResponseDto($"Friend request with ID {requestId} not found."));
        }

        if (request.AddresseeId != currentUserId.Value)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponseDto("You can only decline friend requests sent to you."));
        }

        request.Status = "declined";
        request.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return Ok(new MessageResponseDto("Friend request declined."));
    }

    /// <summary>
    /// Remove a mutual friend or cancel a sent friend request.
    /// </summary>
    [HttpDelete("{username}")]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RemoveFriend(string username, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var normalizedUsername = username.Trim().ToLower();
        var targetUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == normalizedUsername, ct);

        if (targetUser is null)
        {
            return NotFound(new MessageResponseDto($"User '{username}' was not found."));
        }

        var friendship = await _context.Friendships
            .FirstOrDefaultAsync(f =>
                (f.RequesterId == currentUserId.Value && f.AddresseeId == targetUser.Id) ||
                (f.RequesterId == targetUser.Id && f.AddresseeId == currentUserId.Value), ct);

        if (friendship is null)
        {
            return NotFound(new MessageResponseDto($"No friendship or pending request found with '{username}'."));
        }

        _context.Friendships.Remove(friendship);
        await _context.SaveChangesAsync(ct);

        return Ok(new MessageResponseDto($"Friendship or request with '{username}' was removed."));
    }

    /// <summary>
    /// Get all current mutual friends of the authenticated user.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<FriendDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyFriends(CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var friendships = await _context.Friendships
            .AsNoTracking()
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .Where(f => f.Status == "accepted" &&
                        (f.RequesterId == currentUserId.Value || f.AddresseeId == currentUserId.Value))
            .ToListAsync(ct);

        var dtos = friendships.Select(f =>
        {
            var isRequester = f.RequesterId == currentUserId.Value;
            var friendUser = isRequester ? f.Addressee : f.Requester;

            return new FriendDto
            {
                FriendshipId = f.Id,
                UserId = friendUser.Id,
                Username = friendUser.Username,
                Bio = friendUser.Bio,
                FriendsSince = f.UpdatedAt ?? f.CreatedAt
            };
        })
        .OrderBy(f => f.Username)
        .ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Get incoming pending friend requests.
    /// </summary>
    [HttpGet("requests/incoming")]
    [ProducesResponseType(typeof(List<FriendRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetIncomingRequests(CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var requests = await _context.Friendships
            .AsNoTracking()
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .Where(f => f.AddresseeId == currentUserId.Value && f.Status == "pending")
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FriendRequestDto
            {
                Id = f.Id,
                RequesterId = f.RequesterId,
                RequesterUsername = f.Requester.Username,
                RequesterBio = f.Requester.Bio,
                AddresseeId = f.AddresseeId,
                AddresseeUsername = f.Addressee.Username,
                Status = f.Status,
                CreatedAt = f.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(requests);
    }

    /// <summary>
    /// Get outgoing pending friend requests.
    /// </summary>
    [HttpGet("requests/outgoing")]
    [ProducesResponseType(typeof(List<FriendRequestDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetOutgoingRequests(CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var requests = await _context.Friendships
            .AsNoTracking()
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .Where(f => f.RequesterId == currentUserId.Value && f.Status == "pending")
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FriendRequestDto
            {
                Id = f.Id,
                RequesterId = f.RequesterId,
                RequesterUsername = f.Requester.Username,
                RequesterBio = f.Requester.Bio,
                AddresseeId = f.AddresseeId,
                AddresseeUsername = f.Addressee.Username,
                Status = f.Status,
                CreatedAt = f.CreatedAt
            })
            .ToListAsync(ct);

        return Ok(requests);
    }

    /// <summary>
    /// Get friends of a specified user (subject to profile privacy).
    /// </summary>
    [HttpGet("/api/v1/users/{username}/friends")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<FriendDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserFriends(string username, CancellationToken ct)
    {
        var normalizedUsername = username.Trim().ToLower();
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username.ToLower() == normalizedUsername, ct);

        if (user is null)
        {
            return NotFound(new MessageResponseDto($"User '{username}' was not found."));
        }

        var currentUserId = GetCurrentUserId();
        var isSelf = currentUserId.HasValue && currentUserId.Value == user.Id;

        // Check if mutual friends if user profile is private
        if (user.IsPrivate && !isSelf)
        {
            var isMutualFriend = currentUserId.HasValue && await _context.Friendships
                .AnyAsync(f => f.Status == "accepted" &&
                               ((f.RequesterId == currentUserId.Value && f.AddresseeId == user.Id) ||
                                (f.RequesterId == user.Id && f.AddresseeId == currentUserId.Value)), ct);

            if (!isMutualFriend)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new MessageResponseDto("This user's profile is private. You must be mutual friends to view their friend list."));
            }
        }

        var friendships = await _context.Friendships
            .AsNoTracking()
            .Include(f => f.Requester)
            .Include(f => f.Addressee)
            .Where(f => f.Status == "accepted" &&
                        (f.RequesterId == user.Id || f.AddresseeId == user.Id))
            .ToListAsync(ct);

        var dtos = friendships.Select(f =>
        {
            var isRequester = f.RequesterId == user.Id;
            var friendUser = isRequester ? f.Addressee : f.Requester;

            return new FriendDto
            {
                FriendshipId = f.Id,
                UserId = friendUser.Id,
                Username = friendUser.Username,
                Bio = friendUser.Bio,
                FriendsSince = f.UpdatedAt ?? f.CreatedAt
            };
        })
        .OrderBy(f => f.Username)
        .ToList();

        return Ok(dtos);
    }

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? id : null;
    }
}
