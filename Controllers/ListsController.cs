using System.Security.Claims;
using Kue.Api.Data;
using Kue.Api.Dtos.Common;
using Kue.Api.Dtos.Library;
using Kue.Api.Dtos.Lists;
using Kue.Api.Dtos.Media;
using Kue.Api.Entities;
using Kue.Api.Services.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public class ListsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IMediaService _mediaService;
    private readonly ILogger<ListsController> _logger;

    public ListsController(AppDbContext context, IMediaService mediaService, ILogger<ListsController> logger)
    {
        _context = context;
        _mediaService = mediaService;
        _logger = logger;
    }

    /// <summary>
    /// Explore public custom lists across all users.
    /// </summary>
    [HttpGet("lists")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResponseDto<CustomListDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPublicLists(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] int? userId = null,
        CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 20;

        var query = _context.CustomLists
            .AsNoTracking()
            .Where(l => l.IsPublic);

        if (userId.HasValue)
        {
            query = query.Where(l => l.UserId == userId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(l => EF.Functions.ILike(l.Name, $"%{s}%") || (l.Description != null && EF.Functions.ILike(l.Description, $"%{s}%")));
        }

        var totalCount = await query.CountAsync(ct);

        var lists = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new CustomListDto
            {
                Id = l.Id,
                UserId = l.UserId,
                Username = l.User.Username,
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
    /// Get custom lists owned by the current authenticated user (both public and private).
    /// </summary>
    [HttpGet("me/lists")]
    [Authorize]
    [ProducesResponseType(typeof(PagedResponseDto<CustomListDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyLists(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        if (page < 1) page = 1;
        if (pageSize is < 1 or > 50) pageSize = 20;

        var query = _context.CustomLists
            .AsNoTracking()
            .Where(l => l.UserId == currentUserId.Value);

        var totalCount = await query.CountAsync(ct);

        var lists = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new CustomListDto
            {
                Id = l.Id,
                UserId = l.UserId,
                Username = l.User.Username,
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
    /// Create a new custom list for the current user.
    /// </summary>
    [HttpPost("me/lists")]
    [Authorize]
    [ProducesResponseType(typeof(CustomListDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CreateList([FromBody] CreateCustomListRequest request, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == currentUserId.Value, ct);
        if (user is null) return Unauthorized();

        var list = new CustomList
        {
            UserId = currentUserId.Value,
            Name = request.Name.Trim(),
            Description = request.Description?.Trim(),
            IsPublic = request.IsPublic,
            CreatedAt = DateTime.UtcNow
        };

        _context.CustomLists.Add(list);
        await _context.SaveChangesAsync(ct);

        var dto = new CustomListDto
        {
            Id = list.Id,
            UserId = list.UserId,
            Username = user.Username,
            Name = list.Name,
            Description = list.Description,
            IsPublic = list.IsPublic,
            ItemCount = 0,
            PreviewCoverImages = [],
            CreatedAt = list.CreatedAt,
            UpdatedAt = list.UpdatedAt
        };

        return CreatedAtAction(nameof(GetListById), new { listId = list.Id }, dto);
    }

    /// <summary>
    /// Get details of a custom list, including all items and media details.
    /// </summary>
    [HttpGet("lists/{listId:int}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(CustomListDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetListById(int listId, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();

        var list = await _context.CustomLists
            .AsNoTracking()
            .Include(l => l.User)
            .Include(l => l.Items.OrderBy(i => i.Order))
                .ThenInclude(i => i.Media)
            .FirstOrDefaultAsync(l => l.Id == listId, ct);

        if (list is null)
        {
            return NotFound(new MessageResponseDto($"List with ID {listId} not found."));
        }

        if (!list.IsPublic && list.UserId != currentUserId)
        {
            return NotFound(new MessageResponseDto($"List with ID {listId} not found."));
        }

        var itemsDto = list.Items
            .OrderBy(i => i.Order)
            .Select(MapItemToDto)
            .ToList();

        var previewImages = itemsDto
            .Where(i => i.Media.CoverImage != null)
            .Select(i => i.Media.CoverImage!)
            .Take(4)
            .ToList();

        var dto = new CustomListDetailDto
        {
            Id = list.Id,
            UserId = list.UserId,
            Username = list.User.Username,
            Name = list.Name,
            Description = list.Description,
            IsPublic = list.IsPublic,
            ItemCount = itemsDto.Count,
            PreviewCoverImages = previewImages,
            CreatedAt = list.CreatedAt,
            UpdatedAt = list.UpdatedAt,
            Items = itemsDto
        };

        return Ok(dto);
    }

    /// <summary>
    /// Update metadata (name, description, visibility) of a custom list.
    /// </summary>
    [HttpPut("lists/{listId:int}")]
    [HttpPut("me/lists/{listId:int}")]
    [Authorize]
    [ProducesResponseType(typeof(CustomListDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateList(int listId, [FromBody] UpdateCustomListRequest request, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var list = await _context.CustomLists
            .Include(l => l.User)
            .Include(l => l.Items.OrderBy(i => i.Order))
                .ThenInclude(i => i.Media)
            .FirstOrDefaultAsync(l => l.Id == listId, ct);

        if (list is null)
        {
            return NotFound(new MessageResponseDto($"List with ID {listId} not found."));
        }

        if (list.UserId != currentUserId.Value)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponseDto("You can only edit your own lists."));
        }

        list.Name = request.Name.Trim();
        list.Description = request.Description?.Trim();
        list.IsPublic = request.IsPublic;
        list.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        var previewImages = list.Items
            .OrderBy(i => i.Order)
            .Where(i => i.Media.CoverImage != null)
            .Select(i => i.Media.CoverImage!)
            .Take(4)
            .ToList();

        var dto = new CustomListDto
        {
            Id = list.Id,
            UserId = list.UserId,
            Username = list.User.Username,
            Name = list.Name,
            Description = list.Description,
            IsPublic = list.IsPublic,
            ItemCount = list.Items.Count,
            PreviewCoverImages = previewImages,
            CreatedAt = list.CreatedAt,
            UpdatedAt = list.UpdatedAt
        };

        return Ok(dto);
    }

    /// <summary>
    /// Delete a custom list.
    /// </summary>
    [HttpDelete("lists/{listId:int}")]
    [HttpDelete("me/lists/{listId:int}")]
    [Authorize]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteList(int listId, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var list = await _context.CustomLists.FirstOrDefaultAsync(l => l.Id == listId, ct);
        if (list is null)
        {
            return NotFound(new MessageResponseDto($"List with ID {listId} not found."));
        }

        if (list.UserId != currentUserId.Value)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponseDto("You can only delete your own lists."));
        }

        _context.CustomLists.Remove(list);
        await _context.SaveChangesAsync(ct);

        return Ok(new MessageResponseDto("List deleted successfully."));
    }

    /// <summary>
    /// Get the items in a custom list ordered by Order ascending.
    /// </summary>
    [HttpGet("lists/{listId:int}/items")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(List<CustomListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetListItems(int listId, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();

        var list = await _context.CustomLists
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == listId, ct);

        if (list is null)
        {
            return NotFound(new MessageResponseDto($"List with ID {listId} not found."));
        }

        if (!list.IsPublic && list.UserId != currentUserId)
        {
            return NotFound(new MessageResponseDto($"List with ID {listId} not found."));
        }

        var items = await _context.CustomListItems
            .AsNoTracking()
            .Where(i => i.ListId == listId)
            .OrderBy(i => i.Order)
            .ThenBy(i => i.AddedAt)
            .Include(i => i.Media)
            .Select(i => MapItemToDto(i))
            .ToListAsync(ct);

        return Ok(items);
    }

    /// <summary>
    /// Add an item (movie, series, anime, manga, game) to a custom list. Supports JIT ingestion if media is not yet in the database.
    /// </summary>
    [HttpPost("lists/{listId:int}/items")]
    [HttpPost("me/lists/{listId:int}/items")]
    [Authorize]
    [ProducesResponseType(typeof(CustomListItemDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AddListItem(int listId, [FromBody] AddCustomListItemRequest request, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var list = await _context.CustomLists.FirstOrDefaultAsync(l => l.Id == listId, ct);
        if (list is null)
        {
            return NotFound(new MessageResponseDto($"List with ID {listId} not found."));
        }

        if (list.UserId != currentUserId.Value)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponseDto("You can only add items to your own lists."));
        }

        // 1. Resolve or ingest media
        Entities.Media? media = null;
        if (request.MediaId.HasValue && request.MediaId.Value > 0)
        {
            media = await _context.Media.FirstOrDefaultAsync(m => m.Id == request.MediaId.Value, ct);
            if (media is null)
            {
                return NotFound(new MessageResponseDto($"Media with ID {request.MediaId.Value} not found."));
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.MediaType))
            {
                return BadRequest(new MessageResponseDto("MediaType is required when MediaId is not provided."));
            }

            var libraryReq = new AddToLibraryRequest
            {
                MediaType = request.MediaType,
                ExternalSource = request.ExternalSource,
                ExternalId = request.ExternalId,
                Title = request.Title,
                CoverImage = request.CoverImage,
                Year = request.Year,
                Score = request.Score,
                TotalUnits = request.TotalUnits,
                UnitName = request.UnitName,
                RuntimeMinutes = request.RuntimeMinutes,
                Status = "planning"
            };

            try
            {
                media = await _mediaService.GetOrCreateMediaAsync(libraryReq, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resolve or ingest media for custom list {ListId}", listId);
                return BadRequest(new MessageResponseDto("Could not ingest or find the specified media."));
            }
        }

        // 2. Check if media is already in this list
        var alreadyInList = await _context.CustomListItems
            .AnyAsync(i => i.ListId == listId && i.MediaId == media.Id, ct);

        if (alreadyInList)
        {
            return BadRequest(new MessageResponseDto("This media is already in the list."));
        }

        // 3. Determine order
        int order;
        if (request.Order.HasValue)
        {
            order = request.Order.Value;
        }
        else
        {
            var maxOrder = await _context.CustomListItems
                .Where(i => i.ListId == listId)
                .Select(i => (int?)i.Order)
                .MaxAsync(ct);

            order = (maxOrder ?? 0) + 1;
        }

        var item = new CustomListItem
        {
            ListId = listId,
            MediaId = media.Id,
            Order = order,
            Notes = request.Notes?.Trim(),
            AddedAt = DateTime.UtcNow
        };

        _context.CustomListItems.Add(item);
        list.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        // Load media navigation for response
        item.Media = media;

        var dto = MapItemToDto(item);
        return CreatedAtAction(nameof(GetListItems), new { listId }, dto);
    }

    /// <summary>
    /// Update notes or order of an item in a custom list.
    /// </summary>
    [HttpPut("lists/{listId:int}/items/{mediaId:int}")]
    [HttpPut("me/lists/{listId:int}/items/{mediaId:int}")]
    [Authorize]
    [ProducesResponseType(typeof(CustomListItemDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> UpdateListItem(int listId, int mediaId, [FromBody] UpdateCustomListItemRequest request, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var list = await _context.CustomLists.FirstOrDefaultAsync(l => l.Id == listId, ct);
        if (list is null)
        {
            return NotFound(new MessageResponseDto($"List with ID {listId} not found."));
        }

        if (list.UserId != currentUserId.Value)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponseDto("You can only edit items in your own lists."));
        }

        var item = await _context.CustomListItems
            .Include(i => i.Media)
            .FirstOrDefaultAsync(i => i.ListId == listId && i.MediaId == mediaId, ct);

        if (item is null)
        {
            return NotFound(new MessageResponseDto($"Media with ID {mediaId} was not found in this list."));
        }

        if (request.Notes != null)
        {
            item.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        }

        if (request.Order.HasValue)
        {
            item.Order = request.Order.Value;
        }

        list.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return Ok(MapItemToDto(item));
    }

    /// <summary>
    /// Remove an item from a custom list.
    /// </summary>
    [HttpDelete("lists/{listId:int}/items/{mediaId:int}")]
    [HttpDelete("me/lists/{listId:int}/items/{mediaId:int}")]
    [Authorize]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RemoveListItem(int listId, int mediaId, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var list = await _context.CustomLists.FirstOrDefaultAsync(l => l.Id == listId, ct);
        if (list is null)
        {
            return NotFound(new MessageResponseDto($"List with ID {listId} not found."));
        }

        if (list.UserId != currentUserId.Value)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponseDto("You can only remove items from your own lists."));
        }

        var item = await _context.CustomListItems
            .FirstOrDefaultAsync(i => i.ListId == listId && i.MediaId == mediaId, ct);

        if (item is null)
        {
            return NotFound(new MessageResponseDto($"Media with ID {mediaId} was not found in this list."));
        }

        _context.CustomListItems.Remove(item);
        list.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return Ok(new MessageResponseDto("Media removed from list successfully."));
    }

    /// <summary>
    /// Bulk reorder items in a custom list. Accepts ordered array of media IDs.
    /// </summary>
    [HttpPost("lists/{listId:int}/reorder")]
    [HttpPost("me/lists/{listId:int}/reorder")]
    [Authorize]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(MessageResponseDto), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReorderList(int listId, [FromBody] ReorderCustomListRequest request, CancellationToken ct)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId is null) return Unauthorized();

        var list = await _context.CustomLists.FirstOrDefaultAsync(l => l.Id == listId, ct);
        if (list is null)
        {
            return NotFound(new MessageResponseDto($"List with ID {listId} not found."));
        }

        if (list.UserId != currentUserId.Value)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new MessageResponseDto("You can only reorder items in your own lists."));
        }

        var items = await _context.CustomListItems
            .Where(i => i.ListId == listId)
            .ToListAsync(ct);

        for (int i = 0; i < request.MediaIds.Count; i++)
        {
            var mediaId = request.MediaIds[i];
            var item = items.FirstOrDefault(x => x.MediaId == mediaId);
            if (item != null)
            {
                item.Order = i + 1;
            }
        }

        list.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        return Ok(new MessageResponseDto("List items reordered successfully."));
    }

    private int? GetCurrentUserId()
    {
        var idClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(idClaim, out var id) ? id : null;
    }

    private static CustomListItemDto MapItemToDto(CustomListItem item)
    {
        return new CustomListItemDto
        {
            Id = item.Id,
            ListId = item.ListId,
            MediaId = item.MediaId,
            Order = item.Order,
            Notes = item.Notes,
            AddedAt = item.AddedAt,
            Media = new CustomListMediaSummaryDto
            {
                Id = item.Media.Id,
                Title = item.Media.Title,
                MediaType = item.Media.MediaType,
                CoverImage = item.Media.CoverImage,
                Year = item.Media.Year,
                Score = item.Media.Score,
                TotalUnits = item.Media.TotalUnits,
                UnitName = item.Media.UnitName,
                RuntimeMinutes = item.Media.RuntimeMinutes,
                Platforms = item.Media.Platforms
            }
        };
    }
}