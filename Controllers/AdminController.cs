using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
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
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public AdminController(
        AppDbContext context,
        IExternalMediaService externalMediaService,
        IMediaService mediaService,
        ILogger<AdminController> logger,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        _context = context;
        _externalMediaService = externalMediaService;
        _mediaService = mediaService;
        _logger = logger;
        _environment = environment;
        _configuration = configuration;
    }

    // ==========================================
    // DASHBOARD & SERVER HEALTH
    // ==========================================

    /// <summary>
    /// Aggregate counters plus process/database health for the admin overview.
    /// </summary>
    [HttpGet("dashboard")]
    [Authorize(Roles = "Admin,admin")]
    [ProducesResponseType(typeof(AdminDashboardDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDashboard(CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var stats = new AdminStatsDto
        {
            TotalUsers = await _context.Users.CountAsync(ct),
            BannedUsers = await _context.Users.CountAsync(u => u.IsBanned, ct),
            AdminUsers = await _context.Users.CountAsync(
                u => u.Roles.Contains("Admin") || u.Roles.Contains("admin"), ct),
            NewUsersLast7Days = await _context.Users.CountAsync(u => u.CreatedAt >= now.AddDays(-7), ct),
            ActiveSessions = await _context.RefreshTokens.CountAsync(
                r => !r.IsRevoked && r.RevokedAtUtc == null && r.ExpiresAt > now, ct),
            TotalMedia = await _context.Media.IgnoreQueryFilters().CountAsync(ct),
            DeletedMedia = await _context.Media.IgnoreQueryFilters().CountAsync(m => m.IsDeleted, ct),
            LibraryEntries = await _context.LibraryEntries.IgnoreQueryFilters().CountAsync(ct),
            Reviews = await _context.Reviews.IgnoreQueryFilters().CountAsync(ct),
            CustomLists = await _context.CustomLists.CountAsync(ct),
            UserActivities = await _context.UserActivities.CountAsync(ct),
            ImportJobs = await _context.ImportJobs.CountAsync(ct),
        };

        var server = await BuildServerHealthAsync(ct);
        var recentUsers = await _context.Users
            .AsNoTracking()
            .OrderByDescending(u => u.CreatedAt)
            .Take(5)
            .Select(UserProjection(now))
            .ToListAsync(ct);

        return Ok(new AdminDashboardDto
        {
            Stats = stats,
            Server = server,
            RecentUsers = recentUsers,
        });
    }

    private async Task<ServerHealthDto> BuildServerHealthAsync(CancellationToken ct)
    {
        var process = Process.GetCurrentProcess();
        var startedAt = TryGetStartTime(process);
        var uptime = DateTime.UtcNow - startedAt;

        var dbStopwatch = Stopwatch.StartNew();
        bool databaseReachable;
        try
        {
            databaseReachable = await _context.Database.CanConnectAsync(ct);
        }
        catch
        {
            databaseReachable = false;
        }
        dbStopwatch.Stop();

        long? databaseSizeBytes = null;
        var pendingMigrations = 0;
        string? latestMigration = null;

        if (databaseReachable)
        {
            try
            {
                databaseSizeBytes = await _context.Database
                    .SqlQueryRaw<long>("SELECT pg_database_size(current_database()) AS \"Value\"")
                    .SingleAsync(ct);
            }
            catch
            {
                databaseSizeBytes = null;
            }

            try
            {
                pendingMigrations = (await _context.Database.GetPendingMigrationsAsync(ct)).Count();
                latestMigration = (await _context.Database.GetAppliedMigrationsAsync(ct)).LastOrDefault();
            }
            catch
            {
                // Leave the defaults when the migration history cannot be read.
            }
        }

        return new ServerHealthDto
        {
            Status = databaseReachable ? "healthy" : "degraded",
            CheckedAt = DateTimeOffset.UtcNow,
            Environment = _environment.EnvironmentName,
            Runtime = RuntimeInformation.FrameworkDescription,
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
            MachineName = Environment.MachineName,
            ProcessorCount = Environment.ProcessorCount,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            OsDescription = RuntimeInformation.OSDescription,
            StartedAt = new DateTimeOffset(startedAt, TimeSpan.Zero),
            UptimeSeconds = (long)Math.Max(0, uptime.TotalSeconds),
            WorkingSetBytes = process.WorkingSet64,
            GcHeapBytes = GC.GetTotalMemory(false),
            ThreadCount = process.Threads.Count,
            DatabaseReachable = databaseReachable,
            DatabaseLatencyMs = dbStopwatch.ElapsedMilliseconds,
            DatabaseName = _context.Database.GetDbConnection().Database,
            DatabaseProvider = _context.Database.ProviderName,
            DatabaseSizeBytes = databaseSizeBytes,
            PendingMigrations = pendingMigrations,
            LatestMigration = latestMigration,
            Render = ReadRenderInfo(),
            Integrations = new IntegrationStatusDto
            {
                Tmdb = !string.IsNullOrWhiteSpace(_configuration["Tmdb:BearerToken"])
                       || !string.IsNullOrWhiteSpace(_configuration["Tmdb:ApiKey"]),
                Igdb = !string.IsNullOrWhiteSpace(_configuration["Igdb:ClientId"])
                       && !string.IsNullOrWhiteSpace(_configuration["Igdb:ClientSecret"]),
                Brevo = !string.IsNullOrWhiteSpace(_configuration["Brevo:ApiKey"])
                        && !string.IsNullOrWhiteSpace(_configuration["Brevo:SenderEmail"]),
                AniList = true,
            },
        };
    }

    private static DateTime TryGetStartTime(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime();
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }

    private static RenderInfoDto? ReadRenderInfo()
    {
        static string? Env(string name) => Environment.GetEnvironmentVariable(name);

        var serviceName = Env("RENDER_SERVICE_NAME");
        if (string.IsNullOrWhiteSpace(serviceName) && string.IsNullOrWhiteSpace(Env("RENDER")))
        {
            return null;
        }

        return new RenderInfoDto
        {
            ServiceName = serviceName,
            ServiceId = Env("RENDER_SERVICE_ID"),
            InstanceId = Env("RENDER_INSTANCE_ID"),
            Region = Env("RENDER_REGION"),
            Commit = ShortSha(Env("RENDER_GIT_COMMIT")),
            Branch = Env("RENDER_GIT_BRANCH"),
            Repo = Env("RENDER_GIT_REPO_SLUG"),
            ExternalHostname = Env("RENDER_EXTERNAL_HOSTNAME"),
        };
    }

    private static string? ShortSha(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Length <= 7 ? value : value[..7];
    }

    // ==========================================
    // BOOTSTRAP
    // ==========================================

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
            _logger.LogWarning("Bootstrap granted Admin role to user {UserId}.", user.Id);
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

        var now = DateTime.UtcNow;
        var query = _context.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(u =>
                EF.Functions.ILike(u.Username, $"%{s}%")
                || EF.Functions.ILike(u.Email, $"%{s}%")
                || (u.KueId != null && EF.Functions.ILike(u.KueId, $"%{s}%")));
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
            .Select(UserProjection(now))
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
    /// Full detail for one user, including counts and recent activity.
    /// </summary>
    [HttpGet("users/{id:int}")]
    [Authorize(Roles = "Admin,admin")]
    [ProducesResponseType(typeof(AdminUserDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserById(int id, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(UserProjection(now))
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            return NotFound(new MessageResponseDto($"User with ID {id} not found."));
        }

        var recentActivity = await _context.UserActivities
            .AsNoTracking()
            .Where(a => a.UserId == id)
            .OrderByDescending(a => a.CreatedAt)
            .Take(10)
            .Select(a => new AdminUserActivityDto
            {
                Id = a.Id,
                ActivityType = a.ActivityType,
                Action = a.Action,
                DetailText = a.DetailText,
                FormattedText = a.FormattedText,
                Status = a.Status,
                Progress = a.Progress,
                Rating = a.Rating,
                MediaId = a.MediaId,
                MediaTitle = a.Media != null ? a.Media.Title : null,
                MediaType = a.Media != null ? a.Media.MediaType : null,
                CreatedAt = a.CreatedAt,
            })
            .ToListAsync(ct);

        return Ok(new AdminUserDetailDto
        {
            User = user,
            RecentActivity = recentActivity,
        });
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

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

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
        _logger.LogInformation(
            "Admin {AdminId} changed roles for user {UserId} to {Roles}.",
            currentUserId, user.Id, string.Join(",", user.Roles));

        var updated = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(UserProjection(DateTime.UtcNow))
            .FirstAsync(ct);

        return Ok(updated);
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

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);

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
            var now = DateTime.UtcNow;
            await _context.RefreshTokens
                .Where(r => r.UserId == user.Id && !r.IsRevoked)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.IsRevoked, true)
                    .SetProperty(r => r.RevokedAtUtc, now)
                    .SetProperty(r => r.RevocationReason, RefreshTokenRevocationReason.AccountBanned), ct);
        }
        else
        {
            user.IsBanned = false;
            user.BanReason = null;
            user.BannedAt = null;
        }

        await _context.SaveChangesAsync(ct);
        _logger.LogWarning(
            "Admin {AdminId} set ban state {IsBanned} for user {UserId}.",
            currentUserId, request.IsBanned, user.Id);

        var updated = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(UserProjection(DateTime.UtcNow))
            .FirstAsync(ct);

        return Ok(updated);
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
        _logger.LogWarning("Admin {AdminId} deleted user {UserId}.", currentUserId, user.Id);

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
            .Select(MediaProjection)
            .ToListAsync(ct);

        // The LibraryEntry query filter hides entries whose media is soft-deleted,
        // so the projection reports 0 for those rows. Count references without the
        // filter for the admin catalog.
        if (mediaList.Count > 0)
        {
            var mediaIds = mediaList.Select(m => m.Id).ToList();
            var trackedCounts = await _context.LibraryEntries
                .IgnoreQueryFilters()
                .Where(entry => mediaIds.Contains(entry.MediaId))
                .GroupBy(entry => entry.MediaId)
                .Select(group => new { MediaId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(row => row.MediaId, row => row.Count, ct);

            foreach (var media in mediaList)
            {
                media.TrackedCount = trackedCounts.GetValueOrDefault(media.Id);
            }
        }

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
            .IgnoreQueryFilters()
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

        return Ok(await LoadMediaDtoAsync(id, ct));
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

            var media = await _mediaService.GetOrCreateMediaAsync(libraryReq, ct, requireExternalDetails: true);
            return Ok(await LoadMediaDtoAsync(media.Id, ct));
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
            // Library entries, reviews and custom list items use Restrict on the
            // media foreign key. Refuse instead of surfacing a database error or
            // silently destroying user data.
            var libraryCount = await _context.LibraryEntries
                .IgnoreQueryFilters()
                .CountAsync(entry => entry.MediaId == id, ct);
            var reviewCount = await _context.Reviews
                .IgnoreQueryFilters()
                .CountAsync(review => review.MediaId == id, ct);
            var listItemCount = await _context.CustomListItems
                .IgnoreQueryFilters()
                .CountAsync(item => item.MediaId == id, ct);

            if (libraryCount + reviewCount + listItemCount > 0)
            {
                return BadRequest(new MessageResponseDto(
                    $"Cannot permanently delete '{media.Title}' while {libraryCount} library entries, " +
                    $"{reviewCount} reviews and {listItemCount} custom list items reference it. " +
                    "Soft-delete it instead."));
            }

            _context.Media.Remove(media);
            await _context.SaveChangesAsync(ct);
            _logger.LogWarning(
                "Admin {AdminId} permanently deleted media {MediaId}.",
                GetCurrentUserId(), media.Id);
            return Ok(new MessageResponseDto($"Media '{media.Title}' was permanently deleted."));
        }

        media.IsDeleted = true;
        media.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Admin {AdminId} soft-deleted media {MediaId}.",
            GetCurrentUserId(), media.Id);

        return Ok(new MessageResponseDto($"Media '{media.Title}' was soft-deleted."));
    }

    /// <summary>
    /// Restore a previously soft-deleted media record.
    /// </summary>
    [HttpPut("media/{id:int}/restore")]
    [Authorize(Roles = "Admin,admin")]
    [ProducesResponseType(typeof(AdminMediaDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RestoreMedia(int id, CancellationToken ct)
    {
        var media = await _context.Media
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.Id == id, ct);

        if (media is null)
        {
            return NotFound(new MessageResponseDto($"Media with ID {id} not found."));
        }

        if (!media.IsDeleted)
        {
            return BadRequest(new MessageResponseDto($"Media '{media.Title}' is not deleted."));
        }

        media.IsDeleted = false;
        media.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Admin {AdminId} restored media {MediaId}.",
            GetCurrentUserId(), media.Id);

        return Ok(await LoadMediaDtoAsync(id, ct));
    }

    // --- Private Helpers ---

    private async Task<AdminMediaDto> LoadMediaDtoAsync(int id, CancellationToken ct)
    {
        return await _context.Media
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.Id == id)
            .Select(MediaProjection)
            .FirstAsync(ct);
    }

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? id : null;
    }

    private static Expression<Func<User, AdminUserDto>> UserProjection(DateTime now)
    {
        return u => new AdminUserDto
        {
            Id = u.Id,
            Username = u.Username,
            Email = u.Email,
            KueId = u.KueId,
            AvatarId = u.AvatarId,
            Bio = u.Bio,
            IsPrivate = u.IsPrivate,
            IsBanned = u.IsBanned,
            BanReason = u.BanReason,
            BannedAt = u.BannedAt,
            Roles = u.Roles,
            CreatedAt = u.CreatedAt,
            IsTermsAccepted = u.IsTermsAccepted,
            TermsAcceptedAt = u.TermsAcceptedAt,
            TermsVersion = u.TermsVersion,
            GameBalance = u.GameBalance,
            GameTotalEarned = u.GameTotalEarned,
            LibraryCount = u.LibraryEntries.Count,
            ListsCount = u.CustomLists.Count,
            ReviewsCount = u.Reviews.Count,
            FriendsCount = u.SentFriendRequests.Count(f => f.Status == "accepted")
                           + u.ReceivedFriendRequests.Count(f => f.Status == "accepted"),
            ActivitiesCount = u.Activities.Count,
            NotificationsCount = u.Notifications.Count,
            ActiveSessions = u.RefreshTokens.Count(r => !r.IsRevoked && r.RevokedAtUtc == null && r.ExpiresAt > now),
            LastActivityAt = u.Activities.Max(a => (DateTime?)a.CreatedAt),
        };
    }

    private static readonly Expression<Func<Entities.Media, AdminMediaDto>> MediaProjection =
        m => new AdminMediaDto
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
            TrackedCount = m.LibraryEntries.Count,
        };
}
