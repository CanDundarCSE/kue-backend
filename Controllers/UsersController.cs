using System.Security.Claims;
using Kue.Api.Data;
using Kue.Api.Dtos.Common;
using Kue.Api.Dtos.Library;
using Kue.Api.Dtos.Lists;
using Kue.Api.Dtos.Media;
using Kue.Api.Dtos.Ratings;
using Kue.Api.Dtos.Reviews;
using Kue.Api.Dtos.Stats;
using Kue.Api.Dtos.Users;
using Kue.Api.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Produces("application/json")]
[AllowAnonymous]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly ILogger<UsersController> _logger;

    public UsersController(AppDbContext context, ILogger<UsersController> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get public profile of a user by username. If profile is private and visitor is not a mutual friend, detailed counts are locked.
    /// </summary>
    [HttpGet("{username}")]
    [ProducesResponseType(typeof(PublicUserProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserProfile(string username, CancellationToken ct)
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
        var (isSelf, isMutualFriend, friendshipStatus) = await GetRelationshipAsync(currentUserId, user.Id, ct);

        var dto = new PublicUserProfileDto
        {
            Id = user.Id,
            Username = user.Username,
            Bio = user.Bio,
            IsPrivate = user.IsPrivate,
            IsFriend = isMutualFriend,
            FriendshipStatus = friendshipStatus,
            JoinedAt = user.CreatedAt
        };

        // If profile is private and visitor is neither the user themselves nor a mutual friend, lock detailed counts
        if (user.IsPrivate && !isSelf && !isMutualFriend)
        {
            dto.Counts = null;
            return Ok(dto);
        }

        var libraryQuery = _context.LibraryEntries.Where(e => e.UserId == user.Id);

        var totalItems = await libraryQuery.CountAsync(ct);
        var completed = await libraryQuery.CountAsync(e => e.Status == "completed", ct);
        var inProgress = await libraryQuery.CountAsync(e => e.Status == "watching" || e.Status == "reading" || e.Status == "playing" || e.Status == "in_progress", ct);
        var planned = await libraryQuery.CountAsync(e => e.Status == "planned" || e.Status == "plan_to_watch" || e.Status == "plan_to_read" || e.Status == "plan_to_play" || e.Status == "planning", ct);
        var onHold = await libraryQuery.CountAsync(e => e.Status == "on_hold", ct);
        var dropped = await libraryQuery.CountAsync(e => e.Status == "dropped", ct);
        var favorites = await libraryQuery.CountAsync(e => e.IsFavorite, ct);
        var ratingsCount = await libraryQuery.CountAsync(e => e.Rating != null, ct);

        // Only count public lists unless self
        var listsCount = await _context.CustomLists.CountAsync(l => l.UserId == user.Id && (isSelf || l.IsPublic), ct);
        var reviewsCount = await _context.Reviews.CountAsync(r => r.UserId == user.Id, ct);

        dto.Counts = new UserProfileCountsDto
        {
            TotalLibraryItems = totalItems,
            CompletedItems = completed,
            InProgressItems = inProgress,
            PlanningItems = planned,
            OnHoldItems = onHold,
            DroppedItems = dropped,
            FavoritesCount = favorites,
            ListsCount = listsCount,
            ReviewsCount = reviewsCount,
            RatingsCount = ratingsCount
        };

        return Ok(dto);
    }

    /// <summary>
    /// Get the library entries of a user. If profile is private, requires mutual friendship.
    /// </summary>
    [HttpGet("{username}/library")]
    [ProducesResponseType(typeof(PagedResponseDto<LibraryEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserLibrary(
        string username,
        [FromQuery] string? mediaType = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
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
        var (isSelf, isMutualFriend, _) = await GetRelationshipAsync(currentUserId, user.Id, ct);

        if (user.IsPrivate && !isSelf && !isMutualFriend)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponseDto("This user's profile is private. You must be mutual friends to view their library."));
        }

        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 20;

        var query = _context.LibraryEntries
            .AsNoTracking()
            .Include(e => e.Media)
            .Where(e => e.UserId == user.Id);

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            var normType = mediaType.Trim().ToLower();
            query = query.Where(e => e.Media.MediaType.ToLower() == normType);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normStatus = status.Trim().ToLower();
            query = query.Where(e => e.Status.ToLower() == normStatus);
        }

        var totalItems = await query.CountAsync(ct);

        var entries = await query
            .OrderByDescending(e => e.UpdatedAt ?? e.AddedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => MapLibraryEntryToDto(e))
            .ToListAsync(ct);

        return Ok(new PagedResponseDto<LibraryEntryDto>
        {
            Items = entries,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling((double)totalItems / pageSize)
        });
    }

    /// <summary>
    /// Get custom lists of a user. If profile is private, requires mutual friendship. Private lists (isPublic: false) remain strictly visible to the owner only.
    /// </summary>
    [HttpGet("{username}/lists")]
    [ProducesResponseType(typeof(PagedResponseDto<CustomListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserLists(
        string username,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
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
        var (isSelf, isMutualFriend, _) = await GetRelationshipAsync(currentUserId, user.Id, ct);

        if (user.IsPrivate && !isSelf && !isMutualFriend)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponseDto("This user's profile is private. You must be mutual friends to view their lists."));
        }

        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 20;

        // Even for mutual friends, only return isPublic: true lists! Only the user themselves can view their private lists.
        var query = _context.CustomLists
            .AsNoTracking()
            .Where(l => l.UserId == user.Id && (isSelf || l.IsPublic));

        var totalCount = await query.CountAsync(ct);

        var lists = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new CustomListDto
            {
                Id = l.Id,
                UserId = l.UserId,
                Username = user.Username,
                Name = l.Name,
                Description = l.Description,
                IsPublic = l.IsPublic,
                ItemCount = l.Items.Count(),
                PreviewCoverImages = l.Items
                    .OrderBy(i => i.Order)
                    .Where(i => i.Media.CoverImage != null)
                    .Select(i => i.Media.CoverImage!)
                    .Take(4)
                    .ToList(),
                CreatedAt = l.CreatedAt,
                UpdatedAt = l.UpdatedAt
            })
            .ToListAsync(ct);

        return Ok(new PagedResponseDto<CustomListDto>
        {
            Items = lists,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        });
    }

    /// <summary>
    /// Get ratings given by the specified user. If profile is private, requires mutual friendship.
    /// </summary>
    [HttpGet("{username}/ratings")]
    [ProducesResponseType(typeof(PagedResponseDto<UserRatingDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserRatings(
        string username,
        [FromQuery] string? mediaType = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
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
        var (isSelf, isMutualFriend, _) = await GetRelationshipAsync(currentUserId, user.Id, ct);

        if (user.IsPrivate && !isSelf && !isMutualFriend)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponseDto("This user's profile is private. You must be mutual friends to view their ratings."));
        }

        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 20;

        var query = _context.LibraryEntries
            .AsNoTracking()
            .Include(e => e.Media)
            .Where(e => e.UserId == user.Id && e.Rating != null);

        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            var normType = mediaType.Trim().ToLower();
            query = query.Where(e => e.Media.MediaType.ToLower() == normType);
        }

        var totalItems = await query.CountAsync(ct);

        var entries = await query
            .OrderByDescending(e => e.UpdatedAt ?? e.AddedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new UserRatingDto
            {
                MediaId = e.MediaId,
                MediaTitle = e.Media.Title,
                MediaType = e.Media.MediaType,
                MediaCoverImage = e.Media.CoverImage,
                MediaYear = e.Media.Year,
                Rating = e.Rating!.Value,
                RatedAt = e.UpdatedAt ?? e.AddedAt
            })
            .ToListAsync(ct);

        return Ok(new PagedResponseDto<UserRatingDto>
        {
            Items = entries,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling((double)totalItems / pageSize)
        });
    }

    /// <summary>
    /// Get reviews written by the specified user. If profile is private, requires mutual friendship.
    /// </summary>
    [HttpGet("{username}/reviews")]
    [ProducesResponseType(typeof(PagedResponseDto<ReviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserReviews(
        string username,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
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
        var (isSelf, isMutualFriend, _) = await GetRelationshipAsync(currentUserId, user.Id, ct);

        if (user.IsPrivate && !isSelf && !isMutualFriend)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponseDto("This user's profile is private. You must be mutual friends to view their reviews."));
        }

        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 20;

        var query = _context.Reviews
            .AsNoTracking()
            .Include(r => r.Media)
            .Where(r => r.UserId == user.Id);

        var totalItems = await query.CountAsync(ct);

        var reviews = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var likedReviewIds = new HashSet<int>();
        if (currentUserId.HasValue && reviews.Count > 0)
        {
            var reviewIds = reviews.Select(r => r.Id).ToList();
            var liked = await _context.ReviewLikes
                .AsNoTracking()
                .Where(l => l.UserId == currentUserId.Value && reviewIds.Contains(l.ReviewId))
                .Select(l => l.ReviewId)
                .ToListAsync(ct);

            likedReviewIds = [.. liked];
        }

        var dtos = reviews.Select(r => new ReviewDto
        {
            Id = r.Id,
            UserId = user.Id,
            Username = user.Username,
            MediaId = r.Media.Id,
            MediaTitle = r.Media.Title,
            MediaType = r.Media.MediaType,
            MediaCoverImage = r.Media.CoverImage,
            MediaYear = r.Media.Year,
            Content = r.Content,
            Rating = r.Rating,
            ContainsSpoilers = r.ContainsSpoilers,
            LikesCount = r.LikesCount,
            IsLikedByCurrentUser = likedReviewIds.Contains(r.Id),
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        }).ToList();

        return Ok(new PagedResponseDto<ReviewDto>
        {
            Items = dtos,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalItems,
            TotalPages = (int)Math.Ceiling((double)totalItems / pageSize)
        });
    }

    /// <summary>
    /// Get aggregated entertainment stats for the specified user. If profile is private, requires mutual friendship.
    /// </summary>
    [HttpGet("{username}/stats")]
    [ProducesResponseType(typeof(StatsOverviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserStats(string username, CancellationToken ct)
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
        var (isSelf, isMutualFriend, _) = await GetRelationshipAsync(currentUserId, user.Id, ct);

        if (user.IsPrivate && !isSelf && !isMutualFriend)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponseDto("This user's profile is private. You must be mutual friends to view their stats."));
        }

        var entries = await _context.LibraryEntries
            .AsNoTracking()
            .Include(e => e.Media)
            .Where(e => e.UserId == user.Id)
            .ToListAsync(ct);

        var overview = new StatsOverviewDto
        {
            TotalItems = entries.Count,
            TotalCompleted = entries.Count(e => e.Status == "completed"),
            TotalInProgress = entries.Count(e => e.Status is "watching" or "reading" or "playing" or "in_progress"),
            TotalPlanned = entries.Count(e => e.Status is "planned" or "plan_to_watch" or "plan_to_read" or "plan_to_play" or "planning"),
            TotalDropped = entries.Count(e => e.Status == "dropped"),
            TotalOnHold = entries.Count(e => e.Status == "on_hold"),
            TotalFavorites = entries.Count(e => e.IsFavorite),
            MeanScore = CalculateMeanScore(entries.Where(e => e.Rating.HasValue).Select(e => (double)e.Rating!.Value)),

            Anime = BuildTypeBreakdown(entries, "anime"),
            Manga = BuildTypeBreakdown(entries, "manga"),
            Movie = BuildTypeBreakdown(entries, "movie"),
            Series = BuildTypeBreakdown(entries, "series"),
            Game = BuildTypeBreakdown(entries, "game")
        };

        overview.TotalAnimeWatching = overview.Anime.InProgress;
        overview.TotalMangaReading = overview.Manga.InProgress;
        overview.TotalMoviesWatched = overview.Movie.Completed;
        overview.TotalGamesCompleted = overview.Game.Completed;
        overview.TotalSeriesWatching = overview.Series.InProgress;
        overview.AverageAnimeScore = overview.Anime.MeanScore;
        overview.TotalEpisodesWatched = overview.Anime.TotalUnitsConsumed + overview.Series.TotalUnitsConsumed;
        overview.TotalChaptersRead = overview.Manga.TotalUnitsConsumed;

        return Ok(overview);
    }

    /// <summary>
    /// Get favorite and tracked genres distribution for the specified user. If profile is private, requires mutual friendship.
    /// </summary>
    [HttpGet("{username}/genres")]
    [ProducesResponseType(typeof(GenreStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserGenres(string username, CancellationToken ct)
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
        var (isSelf, isMutualFriend, _) = await GetRelationshipAsync(currentUserId, user.Id, ct);

        if (user.IsPrivate && !isSelf && !isMutualFriend)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponseDto("This user's profile is private. You must be mutual friends to view their genre stats."));
        }

        var entries = await _context.LibraryEntries
            .AsNoTracking()
            .Include(e => e.Media)
            .Where(e => e.UserId == user.Id)
            .ToListAsync(ct);

        if (entries.Count == 0)
        {
            return Ok(new GenreStatsDto { Items = [] });
        }

        var genreCounts = entries
            .SelectMany(e => e.Media.Genres)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Genre = group.Key,
                Count = group.Count()
            })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Genre)
            .ToList();

        var totalMediaCount = entries.Count;
        var items = genreCounts.Select(g => new GenreStatItemDto
        {
            Genre = g.Genre,
            Count = g.Count,
            Percentage = totalMediaCount > 0 ? Math.Round((g.Count / (double)totalMediaCount) * 100, 1) : 0
        }).ToList();

        return Ok(new GenreStatsDto { Items = items });
    }

    // --- Private Helpers ---

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? id : null;
    }

    private async Task<(bool isSelf, bool isMutualFriend, string friendshipStatus)> GetRelationshipAsync(
        int? currentUserId, int targetUserId, CancellationToken ct)
    {
        if (!currentUserId.HasValue) return (false, false, "none");
        if (currentUserId.Value == targetUserId) return (true, false, "self");

        var friendship = await _context.Friendships
            .AsNoTracking()
            .FirstOrDefaultAsync(f =>
                (f.RequesterId == currentUserId.Value && f.AddresseeId == targetUserId) ||
                (f.RequesterId == targetUserId && f.AddresseeId == currentUserId.Value), ct);

        if (friendship == null) return (false, false, "none");

        if (friendship.Status == "accepted") return (false, true, "accepted");

        if (friendship.Status == "pending")
        {
            return friendship.RequesterId == currentUserId.Value
                ? (false, false, "pending_sent")
                : (false, false, "pending_received");
        }

        return (false, false, friendship.Status);
    }

    private static LibraryEntryDto MapLibraryEntryToDto(LibraryEntry entry)
    {
        return new LibraryEntryDto
        {
            Id = entry.Id,
            UserId = entry.UserId,
            MediaId = entry.MediaId,
            MediaType = entry.Media.MediaType,
            Status = entry.Status,
            IsFavorite = entry.IsFavorite,
            Rating = entry.Rating,
            Platform = entry.Platform,
            Progress = entry.Progress,
            TotalUnits = entry.Media.TotalUnits,
            UnitName = entry.Media.UnitName,
            AddedAt = entry.AddedAt,
            UpdatedAt = entry.UpdatedAt,
            CompletedAt = entry.CompletedAt,
            Media = new LibraryMediaSummaryDto
            {
                Id = entry.Media.Id,
                Title = entry.Media.Title,
                MediaType = entry.Media.MediaType,
                CoverImage = entry.Media.CoverImage,
                Year = entry.Media.Year,
                Score = entry.Media.Score,
                TotalUnits = entry.Media.TotalUnits,
                UnitName = entry.Media.UnitName,
                RuntimeMinutes = entry.Media.RuntimeMinutes,
                Platforms = entry.Media.Platforms
            }
        };
    }

    private static MediaTypeBreakdownDto BuildTypeBreakdown(List<LibraryEntry> entries, string mediaType)
    {
        var filtered = entries.Where(e => e.Media.MediaType.Equals(mediaType, StringComparison.OrdinalIgnoreCase)).ToList();

        var totalUnits = 0;
        if (mediaType is "anime" or "series" or "manga")
        {
            totalUnits = filtered.Sum(e => e.Progress ?? 0);
        }

        var ratedScores = filtered
            .Where(e => e.Rating.HasValue)
            .Select(e => (double)e.Rating!.Value);

        return new MediaTypeBreakdownDto
        {
            Total = filtered.Count,
            Completed = filtered.Count(e => e.Status == "completed"),
            InProgress = filtered.Count(e => e.Status is "watching" or "reading" or "playing" or "in_progress"),
            Planned = filtered.Count(e => e.Status is "planned" or "plan_to_watch" or "plan_to_read" or "plan_to_play" or "planning"),
            Dropped = filtered.Count(e => e.Status == "dropped"),
            OnHold = filtered.Count(e => e.Status == "on_hold"),
            TotalUnitsConsumed = totalUnits,
            MeanScore = CalculateMeanScore(ratedScores)
        };
    }

    private static double? CalculateMeanScore(IEnumerable<double> scores)
    {
        var list = scores.ToList();
        if (list.Count == 0) return null;
        return Math.Round(list.Average(), 1);
    }
}