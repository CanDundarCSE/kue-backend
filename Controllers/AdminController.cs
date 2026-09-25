using System.Security.Claims;
using Kue.Api.Data;
using Kue.Api.Dtos.Admin;
using Kue.Api.Dtos.Common;
using Kue.Api.Dtos.Library;
using Kue.Api.Dtos.Media;
using Kue.Api.Entities;
using Kue.Api.Services.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/admin")]
[Produces("application/json")]
[Authorize]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IExternalMediaService _externalMediaService;
    private readonly IMediaService _mediaService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        AppDbContext context,
        IExternalMediaService externalMediaService,
        IMediaService mediaService,
        ILogger<AdminController> logger)
    {
        _context = context;
        _externalMediaService = externalMediaService;
        _mediaService = mediaService;
        _logger = logger;
    }

    /// <summary>
    /// Bootstrap initial admin. Can only be invoked when zero admins exist in the database.
    /// </summary>
    [HttpPost("bootstrap")]
    [Authorize]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> BootstrapAdmin(CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        // Safety: If any admin already exists in the system, permanently disable bootstrap
        var anyAdmin = await _context.Users.AnyAsync(u => u.Roles.Contains("Admin") || u.Roles.Contains("admin"), ct);
        if (anyAdmin)
        {
            return BadRequest(new MessageResponseDto("An administrator already exists in the system. Bootstrap is disabled."));
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == currentUserId.Value, ct);
        if (user is null) return Unauthorized();

        if (!user.Roles.Any(r => r.Equals("Admin", StringComparison.OrdinalIgnoreCase)))
        {
            user.Roles.Add("Admin");
            await _context.SaveChangesAsync(ct);
        }

        return Ok(new MessageResponseDto($"User '{user.Username}' has been granted the Admin role. Please log in again to refresh your JWT token with admin claims."));
    }

    // ==========================================
    // USER MANAGEMENT
    // ==========================================

    /// <summary>
    /// List registered users with pagination, search, role, and ban status filters.
    /// </summary>
    [HttpGet("users")]
    [Authorize(Roles = "Admin,admin")]
    [ProducesResponseType(typeof(PagedResponseDto<AdminUserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsers(
        [FromQuery] string? search = null,
        [FromQuery] string? role = null,
        [FromQuery] bool? isBanned = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 100) pageSize = 20;

        var query = _context.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(u => EF.Functions.ILike(u.Username, $"%{s}%") || EF.Functions.ILike(u.Email, $"%{s}%"));
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            var normRole = role.Trim();
            query = query.Where(u => u.Roles.Contains(normRole));
        }

        if (isBanned.HasValue)
        {
            query = query.Where(u => u.IsBanned == isBanned.Value);
        }

        var totalItems = await query.CountAsync(ct);

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                Bio = u.Bio,
                IsPrivate = u.IsPrivate,
                IsBanned = u.IsBanned,
                BanReason = u.BanReason,
                BannedAt = u.BannedAt,
                Roles = u.Roles,
                CreatedAt = u.CreatedAt,
                LibraryCount = u.LibraryEntries.Count,
                ReviewsCount = u.Reviews.Count,
                ListsCount = u.CustomLists.Count
            })
            .ToListAsync(ct);

        return Ok(new PagedResponseDto<AdminUserDto>
        {
            Items = users,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling((double)totalItems / pageSize)
        });
    }

    /// <summary>
    /// Get details of a specific user by ID.
    /// </summary>
    [HttpGet("users/{id:int}")]
    [Authorize(Roles = "Admin,admin")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserById(int id, CancellationToken ct)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new AdminUserDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                Bio = u.Bio,
                IsPrivate = u.IsPrivate,
                IsBanned = u.IsBanned,
                BanReason = u.BanReason,
                BannedAt = u.BannedAt,
                Roles = u.Roles,
                CreatedAt = u.CreatedAt,
                LibraryCount = u.LibraryEntries.Count,
                ReviewsCount = u.Reviews.Count,
                ListsCount = u.CustomLists.Count
            })
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            return NotFound(new MessageResponseDto($"User with ID {id} not found."));
        }

        return Ok(user);
    }

    /// <summary>
    /// Update roles for a user.
    /// </summary>
    [HttpPut("users/{id:int}/role")]
    [Authorize(Roles = "Admin,admin")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUserRole(int id, [FromBody] UpdateUserRoleRequest request, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();

        if (request.Roles.Count == 0)
        {
            return BadRequest(new MessageResponseDto("At least one role must be specified."));
        }

        var user = await _context.Users
            .Include(u => u.LibraryEntries)
            .Include(u => u.Reviews)
            .Include(u => u.CustomLists)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null)
        {
            return NotFound(new MessageResponseDto($"User with ID {id} not found."));
        }

        // Safety: Admin cannot revoke their own Admin role
        if (currentUserId.HasValue && user.Id == currentUserId.Value &&
            !request.Roles.Any(r => r.Equals("Admin", StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(new MessageResponseDto("You cannot remove the Admin role from yourself."));
        }

        user.Roles = request.Roles.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        await _context.SaveChangesAsync(ct);

        return Ok(MapToAdminUserDto(user));
    }

    /// <summary>
    /// Ban or unban a user. Banning immediately revokes all active refresh tokens.
    /// </summary>
    [HttpPut("users/{id:int}/ban")]
    [Authorize(Roles = "Admin,admin")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BanUser(int id, [FromBody] BanUserRequest request, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();

        if (currentUserId.HasValue && id == currentUserId.Value && request.IsBanned)
        {
            return BadRequest(new MessageResponseDto("You cannot ban your own account."));
        }

        var user = await _context.Users
            .Include(u => u.LibraryEntries)
            .Include(u => u.Reviews)
            .Include(u => u.CustomLists)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        if (user is null)
        {
            return NotFound(new MessageResponseDto($"User with ID {id} not found."));
        }

        if (request.IsBanned)
        {
            user.IsBanned = true;
            user.BanReason = request.Reason?.Trim();
            user.BannedAt = DateTime.UtcNow;

            // Revoke all active refresh tokens for immediate session invalidation
            var activeTokens = await _context.RefreshTokens
                .Where(r => r.UserId == user.Id && !r.IsRevoked)
                .ToListAsync(ct);

            foreach (var token in activeTokens)
            {
                token.IsRevoked = true;
                token.RevokedAt = DateTime.UtcNow;
            }
        }
        else
        {
            user.IsBanned = false;
            user.BanReason = null;
            user.BannedAt = null;
        }

        await _context.SaveChangesAsync(ct);

        return Ok(MapToAdminUserDto(user));
    }

    /// <summary>
    /// Permanently delete a user account and associated tracking data.
    /// </summary>
    [HttpDelete("users/{id:int}")]
    [Authorize(Roles = "Admin,admin")]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteUser(int id, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();

        if (currentUserId.HasValue && id == currentUserId.Value)
        {
            return BadRequest(new MessageResponseDto("You cannot delete your own account from the Admin panel."));
        }

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
        {
            return NotFound(new MessageResponseDto($"User with ID {id} not found."));
        }

        _context.Users.Remove(user);
        await _context.SaveChangesAsync(ct);

        return Ok(new MessageResponseDto($"User '{user.Username}' and associated data were permanently deleted."));
    }

    // ==========================================
    // MEDIA METADATA & SYNCHRONIZATION
    // ==========================================

    /// <summary>
    /// List cached media records in the database with usage statistics.
    /// </summary>
    [HttpGet("media")]
    [Authorize(Roles = "Admin,admin")]
    [ProducesResponseType(typeof(PagedResponseDto<AdminMediaDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMedia(
        [FromQuery] string? search = null,
        [FromQuery] string? mediaType = null,
        [FromQuery] string? externalSource = null,
        [FromQuery] bool includeDeleted = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 100) pageSize = 20;

        IQueryable<Entities.Media> query = includeDeleted
            ? _context.Media.IgnoreQueryFilters().AsNoTracking()
            : _context.Media.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(m => EF.Functions.ILike(m.Title, $"%{s}%") || m.ExternalId.Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            var normType = mediaType.Trim().ToLower();
            query = query.Where(m => m.MediaType.ToLower() == normType);
        }

        if (!string.IsNullOrWhiteSpace(externalSource))
        {
            var normSource = externalSource.Trim().ToLower();
            query = query.Where(m => m.ExternalSource.ToLower() == normSource);
        }

        var totalItems = await query.CountAsync(ct);

        var mediaList = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new AdminMediaDto
            {
                Id = m.Id,
                Title = m.Title,
                MediaType = m.MediaType,
                ExternalSource = m.ExternalSource,
                ExternalId = m.ExternalId,
                CoverImage = m.CoverImage,
                Year = m.Year,
                Score = m.Score,
                TotalUnits = m.TotalUnits,
                UnitName = m.UnitName,
                RuntimeMinutes = m.RuntimeMinutes,
                Platforms = m.Platforms,
                IsDeleted = m.IsDeleted,
                CreatedAt = m.CreatedAt,
                UpdatedAt = m.UpdatedAt,
                TrackedCount = m.LibraryEntries.Count
            })
            .ToListAsync(ct);

        return Ok(new PagedResponseDto<AdminMediaDto>
        {
            Items = mediaList,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling((double)totalItems / pageSize)
        });
    }

    /// <summary>
    /// Refresh metadata for a cached media entry by fetching fresh data from the external source (TMDb / AniList / IGDB).
    /// </summary>
    [HttpPost("media/{id:int}/refresh")]
    [Authorize(Roles = "Admin,admin")]
    [ProducesResponseType(typeof(AdminMediaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RefreshMediaById(int id, CancellationToken ct)
    {
        var media = await _context.Media
            .Include(m => m.LibraryEntries)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (media is null)
        {
            return NotFound(new MessageResponseDto($"Media with ID {id} not found."));
        }

        MediaDto? freshDetails = null;
        try
        {
            freshDetails = await _externalMediaService.GetDetailsAsync(media.MediaType, media.ExternalSource, media.ExternalId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh metadata for media {Id} from source {Source}", id, media.ExternalSource);
        }

        if (freshDetails is null)
        {
            return BadRequest(new MessageResponseDto($"External source '{media.ExternalSource}' did not return fresh metadata for {media.MediaType} '{media.ExternalId}'."));
        }

        media.Title = freshDetails.Title;
        media.Description = freshDetails.Description ?? media.Description;
        media.CoverImage = freshDetails.CoverImage ?? media.CoverImage;
        media.BannerImage = freshDetails.BannerImage ?? media.BannerImage;
        media.Year = freshDetails.Year ?? media.Year;
        media.Score = freshDetails.Score ?? media.Score;
        media.Genres = freshDetails.Genres.Count > 0 ? freshDetails.Genres : media.Genres;
        media.TotalUnits = freshDetails.TotalUnits ?? media.TotalUnits;
        media.UnitName = freshDetails.UnitName ?? media.UnitName;
        media.TotalSeasons = freshDetails.TotalSeasons ?? media.TotalSeasons;
        media.TotalVolumes = freshDetails.TotalVolumes ?? media.TotalVolumes;
        media.RuntimeMinutes = freshDetails.RuntimeMinutes ?? media.RuntimeMinutes;
        media.Platforms = freshDetails.Platforms ?? media.Platforms;
        media.Developer = freshDetails.Developer ?? media.Developer;
        media.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        return Ok(MapToAdminMediaDto(media));
    }

    /// <summary>
    /// JIT-synchronize an external media record into the database cache.
    /// </summary>
    [HttpPost("media/sync")]
    [Authorize(Roles = "Admin,admin")]
    [ProducesResponseType(typeof(AdminMediaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SyncMedia([FromBody] SyncMediaRequest request, CancellationToken ct)
    {
        try
        {
            var libraryReq = new AddToLibraryRequest
            {
                MediaType = request.MediaType,
                ExternalSource = request.ExternalSource,
                ExternalId = request.ExternalId,
                Status = "planning"
            };

            var media = await _mediaService.GetOrCreateMediaAsync(libraryReq, ct);
            return Ok(MapToAdminMediaDto(media));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync media {Type} {Id}", request.MediaType, request.ExternalId);
            return BadRequest(new MessageResponseDto($"Could not sync media from {request.ExternalSource}: {ex.Message}"));
        }
    }

    /// <summary>
    /// Soft delete or restore a media record from the database.
    /// </summary>
    [HttpDelete("media/{id:int}")]
    [Authorize(Roles = "Admin,admin")]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMedia(int id, [FromQuery] bool permanent = false, CancellationToken ct = default)
    {
        var media = await _context.Media
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (media is null)
        {
            return NotFound(new MessageResponseDto($"Media with ID {id} not found."));
        }

        if (permanent)
        {
            _context.Media.Remove(media);
            await _context.SaveChangesAsync(ct);
            return Ok(new MessageResponseDto($"Media '{media.Title}' was permanently deleted."));
        }

        media.IsDeleted = true;
        media.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return Ok(new MessageResponseDto($"Media '{media.Title}' was soft-deleted."));
    }

    // --- Private Helpers ---

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? id : null;
    }

    private static AdminUserDto MapToAdminUserDto(User user)
    {
        return new AdminUserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Bio = user.Bio,
            IsPrivate = user.IsPrivate,
            IsBanned = user.IsBanned,
            BanReason = user.BanReason,
            BannedAt = user.BannedAt,
            Roles = user.Roles,
            CreatedAt = user.CreatedAt,
            LibraryCount = user.LibraryEntries?.Count ?? 0,
            ReviewsCount = user.Reviews?.Count ?? 0,
            ListsCount = user.CustomLists?.Count ?? 0
        };
    }

    private static AdminMediaDto MapToAdminMediaDto(Entities.Media media)
    {
        return new AdminMediaDto
        {
            Id = media.Id,
            Title = media.Title,
            MediaType = media.MediaType,
            ExternalSource = media.ExternalSource,
            ExternalId = media.ExternalId,
            CoverImage = media.CoverImage,
            Year = media.Year,
            Score = media.Score,
            TotalUnits = media.TotalUnits,
            UnitName = media.UnitName,
            RuntimeMinutes = media.RuntimeMinutes,
            Platforms = media.Platforms,
            IsDeleted = media.IsDeleted,
            CreatedAt = media.CreatedAt,
            UpdatedAt = media.UpdatedAt,
            TrackedCount = media.LibraryEntries?.Count ?? 0
        };
    }
}