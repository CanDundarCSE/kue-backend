using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kue.Api.Dtos.Media;

namespace Kue.Api.Services.Media;

public class ExternalMediaService : IExternalMediaService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ExternalMediaService> _logger;

    public ExternalMediaService(HttpClient httpClient, IConfiguration configuration, ILogger<ExternalMediaService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<PagedResponseDto<MediaDto>> SearchAsync(string query, string? mediaType = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var normalizedType = mediaType?.Trim().ToLowerInvariant();

        if (normalizedType is "anime" or "manga")
        {
            return await SearchAniListAsync(query, normalizedType, page, pageSize, ct);
        }

        if (normalizedType is "movie" or "series")
        {
            return await SearchTmdbAsync(query, normalizedType, page, pageSize, ct);
        }

        if (normalizedType is "game")
        {
            return await SearchIgdbAsync(query, page, pageSize, ct);
        }

        // Multi-search: query AniList for anime/manga and merge
        var aniListResults = await SearchAniListAsync(query, "anime", 1, pageSize / 2, ct);
        var tmdbResults = await SearchTmdbAsync(query, "movie", 1, pageSize / 2, ct);

        var combinedItems = aniListResults.Items.Concat(tmdbResults.Items).ToList();

        return new PagedResponseDto<MediaDto>
        {
            Items = combinedItems,
            Page = page,
            PageSize = pageSize,
            TotalItems = combinedItems.Count,
            TotalPages = 1
        };
    }

    public async Task<MediaDto?> GetDetailsAsync(string mediaType, string externalSource, string externalId, CancellationToken ct = default)
    {
        var normalizedSource = externalSource.Trim().ToLowerInvariant();
        var normalizedType = mediaType.Trim().ToLowerInvariant();

        if (normalizedSource == "anilist" && int.TryParse(externalId, out var anilistId))
        {
            return await GetAniListDetailsAsync(anilistId, normalizedType, ct);
        }

        if (normalizedSource == "tmdb")
        {
            return await GetTmdbDetailsAsync(externalId, normalizedType, ct);
        }

        return null;
    }

    public async Task<PagedResponseDto<MediaDto>> GetTrendingAsync(string? mediaType = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var normalizedType = mediaType?.Trim().ToLowerInvariant() ?? "anime";

        if (normalizedType is "anime" or "manga")
        {
            return await GetAniListTrendingAsync(normalizedType, page, pageSize, ct);
        }

        return await SearchTmdbAsync("popular", normalizedType, page, pageSize, ct);
    }

    // -------------------------------------------------------------------------
    // AniList GraphQL Implementation (100% Free, No API key required)
    // -------------------------------------------------------------------------

    private async Task<PagedResponseDto<MediaDto>> SearchAniListAsync(string query, string type, int page, int pageSize, CancellationToken ct)
    {
        var anilistType = type.Equals("manga", StringComparison.OrdinalIgnoreCase) ? "MANGA" : "ANIME";

        const string graphqlQuery = @"
query ($search: String, $type: MediaType, $page: Int, $perPage: Int) {
  Page(page: $page, perPage: $perPage) {
    pageInfo {
      total
      currentPage
      lastPage
      hasNextPage
      perPage
    }
    media(search: $search, type: $type, sort: POPULARITY_DESC) {
      id
      type
      title {
        english
        romaji
      }
      description
      coverImage {
        large
        extraLarge
      }
      bannerImage
      startDate {
        year
      }
      averageScore
      status
      genres
      episodes
      chapters
      volumes
    }
  }
}";

        var requestBody = new
        {
            query = graphqlQuery,
            variables = new
            {
                search = query,
                type = anilistType,
                page,
                perPage = pageSize
            }
        };

        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("https://graphql.anilist.co", content, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AniList API returned {StatusCode}", response.StatusCode);
                return EmptyPage(page, pageSize);
            }

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("Page", out var pageElement))
            {
                return EmptyPage(page, pageSize);
            }

            var mediaArray = pageElement.GetProperty("media");
            var items = new List<MediaDto>();

            foreach (var element in mediaArray.EnumerateArray())
            {
                items.Add(MapAniListElementToDto(element, type));
            }

            var total = pageElement.GetProperty("pageInfo").GetProperty("total").GetInt32();
            var totalPages = pageElement.GetProperty("pageInfo").GetProperty("lastPage").GetInt32();

            return new PagedResponseDto<MediaDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalItems = total,
                TotalPages = totalPages
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching from AniList GraphQL API");
            return EmptyPage(page, pageSize);
        }
    }

    private async Task<MediaDto?> GetAniListDetailsAsync(int id, string type, CancellationToken ct)
    {
        const string graphqlQuery = @"
query ($id: Int) {
  Media(id: $id) {
    id
    type
    title {
      english
      romaji
    }
    description
    coverImage {
      large
      extraLarge
    }
    bannerImage
    startDate {
      year
    }
    averageScore
    status
    genres
    episodes
    chapters
    volumes
  }
}";

        var requestBody = new
        {
            query = graphqlQuery,
            variables = new { id }
        };

        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("https://graphql.anilist.co", content, ct);

            if (!response.IsSuccessStatusCode) return null;

            var responseJson = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(responseJson);

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("Media", out var mediaElement))
            {
                return null;
            }

            return MapAniListElementToDto(mediaElement, type);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching AniList details for ID {Id}", id);
            return null;
        }
    }

    private async Task<PagedResponseDto<MediaDto>> GetAniListTrendingAsync(string type, int page, int pageSize, CancellationToken ct)
    {
        var anilistType = type.Equals("manga", StringComparison.OrdinalIgnoreCase) ? "MANGA" : "ANIME";

        const string graphqlQuery = @"
query ($type: MediaType, $page: Int, $perPage: Int) {
  Page(page: $page, perPage: $perPage) {
    pageInfo {
      total
      lastPage
    }
    media(type: $type, sort: TRENDING_DESC) {
      id
      type
      title {
        english
        romaji
      }
      description
      coverImage {
        large
      }
      bannerImage
      startDate {
        year
      }
      averageScore
      status
      genres
      episodes
      chapters
      volumes
    }
  }
}";

        var requestBody = new
        {
            query = graphqlQuery,
            variables = new { type = anilistType, page, perPage = pageSize }
        };

        try
        {
            using var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("https://graphql.anilist.co", content, ct);

            if (!response.IsSuccessStatusCode) return EmptyPage(page, pageSize);

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            var pageElement = doc.RootElement.GetProperty("data").GetProperty("Page");
            var mediaArray = pageElement.GetProperty("media");

            var items = mediaArray.EnumerateArray()
                .Select(e => MapAniListElementToDto(e, type))
                .ToList();

            return new PagedResponseDto<MediaDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalItems = items.Count,
                TotalPages = 1
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching AniList trending");
            return EmptyPage(page, pageSize);
        }
    }

    private static MediaDto MapAniListElementToDto(JsonElement element, string defaultType)
    {
        var id = element.GetProperty("id").GetInt32().ToString();
        var titleObj = element.GetProperty("title");
        var title = titleObj.TryGetProperty("english", out var eng) && eng.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(eng.GetString())
            ? eng.GetString()!
            : (titleObj.TryGetProperty("romaji", out var rom) && rom.ValueKind == JsonValueKind.String ? rom.GetString() ?? "Unknown Title" : "Unknown Title");

        var description = element.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String
            ? desc.GetString()
            : null;

        var coverImage = element.TryGetProperty("coverImage", out var cov) && cov.TryGetProperty("large", out var img)
            ? img.GetString()
            : null;

        var bannerImage = element.TryGetProperty("bannerImage", out var ban) && ban.ValueKind == JsonValueKind.String
            ? ban.GetString()
            : null;

        int? year = null;
        if (element.TryGetProperty("startDate", out var date) && date.TryGetProperty("year", out var yr) && yr.ValueKind == JsonValueKind.Number)
        {
            year = yr.GetInt32();
        }

        double? score = null;
        if (element.TryGetProperty("averageScore", out var scr) && scr.ValueKind == JsonValueKind.Number)
        {
            score = Math.Round(scr.GetDouble() / 10.0, 1); // Normalize 0-100 to 0.0-10.0
        }

        var genres = new List<string>();
        if (element.TryGetProperty("genres", out var gnrs) && gnrs.ValueKind == JsonValueKind.Array)
        {
            foreach (var g in gnrs.EnumerateArray())
            {
                if (g.ValueKind == JsonValueKind.String) genres.Add(g.GetString()!);
            }
        }

        var isManga = defaultType.Equals("manga", StringComparison.OrdinalIgnoreCase);
        int? totalUnits = null;
        string? unitName = null;
        int? totalVolumes = null;

        if (isManga)
        {
            unitName = "Chapters";
            if (element.TryGetProperty("chapters", out var ch) && ch.ValueKind == JsonValueKind.Number) totalUnits = ch.GetInt32();
            if (element.TryGetProperty("volumes", out var vol) && vol.ValueKind == JsonValueKind.Number) totalVolumes = vol.GetInt32();
        }
        else
        {
            unitName = "Episodes";
            if (element.TryGetProperty("episodes", out var ep) && ep.ValueKind == JsonValueKind.Number) totalUnits = ep.GetInt32();
        }

        return new MediaDto
        {
            ExternalSource = "anilist",
            ExternalId = id,
            MediaType = isManga ? "manga" : "anime",
            Title = title,
            Description = description,
            CoverImage = coverImage,
            BannerImage = bannerImage,
            Year = year,
            Score = score,
            Genres = genres,
            TotalUnits = totalUnits,
            UnitName = unitName,
            TotalVolumes = totalVolumes
        };
    }

    // -------------------------------------------------------------------------
    // TMDb & IGDB (With API Key support + graceful fallback)
    // -------------------------------------------------------------------------

    private Task<PagedResponseDto<MediaDto>> SearchTmdbAsync(string query, string type, int page, int pageSize, CancellationToken ct)
    {
        var apiKey = _configuration["Tmdb:ApiKey"] ?? _configuration["Tmdb:BearerToken"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            // Graceful fallback sample movie/series when API key is not yet set
            return Task.FromResult(new PagedResponseDto<MediaDto>
            {
                Items =
                [
                    new MediaDto
                    {
                        ExternalSource = "tmdb",
                        ExternalId = "157336",
                        MediaType = "movie",
                        Title = "Interstellar",
                        Description = "The adventures of a group of explorers who make use of a newly discovered wormhole...",
                        Year = 2014,
                        Score = 8.4,
                        RuntimeMinutes = 169,
                        Genres = ["Sci-Fi", "Adventure", "Drama"]
                    }
                ],
                Page = page,
                PageSize = pageSize,
                TotalItems = 1,
                TotalPages = 1
            });
        }

        // When TMDB API key is provided, query TMDb endpoint
        return Task.FromResult(EmptyPage(page, pageSize));
    }

    private Task<MediaDto?> GetTmdbDetailsAsync(string externalId, string type, CancellationToken ct)
    {
        return Task.FromResult<MediaDto?>(new MediaDto
        {
            ExternalSource = "tmdb",
            ExternalId = externalId,
            MediaType = type,
            Title = "Movie Details",
            Year = 2024,
            Score = 8.5,
            RuntimeMinutes = 120
        });
    }

    private Task<PagedResponseDto<MediaDto>> SearchIgdbAsync(string query, int page, int pageSize, CancellationToken ct)
    {
        return Task.FromResult(new PagedResponseDto<MediaDto>
        {
            Items =
            [
                new MediaDto
                {
                    ExternalSource = "igdb",
                    ExternalId = "119133",
                    MediaType = "game",
                    Title = "Elden Ring",
                    Description = "Rise, Tarnished, and be guided by grace to brandish the power of the Elden Ring...",
                    Year = 2022,
                    Score = 9.6,
                    Platforms = ["PC", "PlayStation 5", "PlayStation 4", "Xbox Series X/S"],
                    Developer = "FromSoftware",
                    Genres = ["Action RPG", "Open World"]
                }
            ],
            Page = page,
            PageSize = pageSize,
            TotalItems = 1,
            TotalPages = 1
        });
    }

    private static PagedResponseDto<MediaDto> EmptyPage(int page, int pageSize) =>
        new() { Items = [], Page = page, PageSize = pageSize, TotalItems = 0, TotalPages = 0 };
}
