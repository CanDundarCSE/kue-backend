using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Kue.Api.Dtos.Media;
using Microsoft.Extensions.Caching.Memory;

namespace Kue.Api.Services.Media;

public class ExternalMediaService : IExternalMediaService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<ExternalMediaService> _logger;

    public ExternalMediaService(
        HttpClient httpClient,
        IConfiguration configuration,
        IMemoryCache memoryCache,
        ILogger<ExternalMediaService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _memoryCache = memoryCache;
        _logger = logger;
    }

    public async Task<PagedResponseDto<MediaDto>> SearchAsync(string query, string? mediaType = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var normalizedType = NormalizeMediaType(mediaType);

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

        // ---------------------------------------------------------------------
        // Unified Multi-Search (Movies, TV Series, Games, Anime & Manga in parallel)
        // ---------------------------------------------------------------------
        var tmdbTask = SearchTmdbAsync(query, "multi", 1, pageSize, ct);
        var igdbTask = SearchIgdbAsync(query, 1, pageSize, ct);
        var animeTask = SearchAniListAsync(query, "anime", 1, Math.Min(10, pageSize), ct);
        var mangaTask = SearchAniListAsync(query, "manga", 1, Math.Min(5, pageSize), ct);

        await Task.WhenAll(tmdbTask, igdbTask, animeTask, mangaTask);

        var tmdbItems = (await tmdbTask).Items;
        var igdbItems = (await igdbTask).Items;
        var animeItems = (await animeTask).Items;
        var mangaItems = (await mangaTask).Items;

        // Interleave results so games, series, movies and anime are evenly represented
        var combinedItems = new List<MediaDto>();
        var maxCount = Math.Max(Math.Max(tmdbItems.Count, igdbItems.Count), Math.Max(animeItems.Count, mangaItems.Count));

        for (int i = 0; i < maxCount; i++)
        {
            if (i < igdbItems.Count) combinedItems.Add(igdbItems[i]);
            if (i < tmdbItems.Count) combinedItems.Add(tmdbItems[i]);
            if (i < animeItems.Count) combinedItems.Add(animeItems[i]);
            if (i < mangaItems.Count) combinedItems.Add(mangaItems[i]);
        }

        var pagedItems = combinedItems
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return new PagedResponseDto<MediaDto>
        {
            Items = pagedItems,
            Page = page,
            PageSize = pageSize,
            TotalItems = combinedItems.Count,
            TotalPages = (int)Math.Ceiling(combinedItems.Count / (double)pageSize)
        };
    }

    public async Task<MediaDto?> GetDetailsAsync(string mediaType, string externalSource, string externalId, CancellationToken ct = default)
    {
        var normalizedSource = externalSource.Trim().ToLowerInvariant();
        var normalizedType = NormalizeMediaType(mediaType) ?? mediaType.Trim().ToLowerInvariant();

        if (normalizedSource == "anilist" && int.TryParse(externalId, out var anilistId))
        {
            return await GetAniListDetailsAsync(anilistId, normalizedType, ct);
        }

        if (normalizedSource == "tmdb")
        {
            return await GetTmdbDetailsAsync(externalId, normalizedType, ct);
        }

        if (normalizedSource == "igdb")
        {
            return await GetIgdbDetailsAsync(externalId, ct);
        }

        return null;
    }

    public async Task<PagedResponseDto<MediaDto>> GetTrendingAsync(string? mediaType = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var normalizedType = NormalizeMediaType(mediaType) ?? "anime";

        if (normalizedType is "anime" or "manga")
        {
            return await GetAniListTrendingAsync(normalizedType, page, pageSize, ct);
        }

        if (normalizedType is "game")
        {
            return await GetIgdbTrendingAsync(page, pageSize, ct);
        }

        return await GetTmdbTrendingAsync(normalizedType, page, pageSize, ct);
    }

    private static string? NormalizeMediaType(string? mediaType)
    {
        if (string.IsNullOrWhiteSpace(mediaType)) return null;
        var t = mediaType.Trim().ToLowerInvariant();
        if (t is "all" or "any" or "both" or "everything" or "multi") return null;

        return t switch
        {
            "tv" or "tvshow" or "tv-show" or "show" or "shows" or "series" => "series",
            "film" or "films" or "movies" or "movie" => "movie",
            "games" or "videogame" or "video-game" or "game" => "game",
            "manga" or "mangas" => "manga",
            "anime" or "animes" => "anime",
            _ => t
        };
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
    // TMDb Integration (Movies & Series)
    // -------------------------------------------------------------------------

    private HttpRequestMessage? CreateTmdbRequest(HttpMethod method, string pathAndQuery)
    {
        var bearerToken = _configuration["Tmdb:BearerToken"];
        var apiKey = _configuration["Tmdb:ApiKey"];

        if (string.IsNullOrWhiteSpace(bearerToken) && string.IsNullOrWhiteSpace(apiKey))
        {
            return null;
        }

        string fullUrl;
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            fullUrl = pathAndQuery.StartsWith("http") ? pathAndQuery : $"https://api.themoviedb.org/3{pathAndQuery}";
        }
        else
        {
            var separator = pathAndQuery.Contains('?') ? "&" : "?";
            fullUrl = pathAndQuery.StartsWith("http") 
                ? $"{pathAndQuery}{separator}api_key={apiKey}" 
                : $"https://api.themoviedb.org/3{pathAndQuery}{separator}api_key={apiKey}";
        }

        var request = new HttpRequestMessage(method, fullUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return request;
    }

    private async Task<PagedResponseDto<MediaDto>> SearchTmdbAsync(string query, string type, int page, int pageSize, CancellationToken ct)
    {
        var normalizedType = type?.Trim().ToLowerInvariant() ?? "multi";
        var isTv = normalizedType is "series" or "tv";
        var isMovie = normalizedType is "movie";

        string endpoint;
        if (isMovie)
        {
            endpoint = $"/search/movie?query={Uri.EscapeDataString(query)}&page={page}&include_adult=false";
        }
        else if (isTv)
        {
            endpoint = $"/search/tv?query={Uri.EscapeDataString(query)}&page={page}&include_adult=false";
        }
        else
        {
            endpoint = $"/search/multi?query={Uri.EscapeDataString(query)}&page={page}&include_adult=false";
        }

        using var request = CreateTmdbRequest(HttpMethod.Get, endpoint);
        if (request == null)
        {
            return GetFallbackSampleTmdb(isTv ? "series" : "movie", page, pageSize);
        }

        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("TMDb API returned status {Status}: {Error}", response.StatusCode, err);
                return EmptyPage(page, pageSize);
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return EmptyPage(page, pageSize);
            }

            var items = new List<MediaDto>();
            foreach (var el in results.EnumerateArray())
            {
                var itemType = isTv ? "series" : (isMovie ? "movie" : (el.TryGetProperty("media_type", out var mt) && mt.GetString() == "tv" ? "series" : "movie"));
                if (el.TryGetProperty("media_type", out var multiType) && multiType.GetString() is not "movie" and not "tv")
                {
                    continue;
                }

                items.Add(MapTmdbItemToDto(el, itemType));
            }

            var totalResults = root.TryGetProperty("total_results", out var tr) ? tr.GetInt32() : items.Count;
            var totalPages = root.TryGetProperty("total_pages", out var tp) ? tp.GetInt32() : 1;

            return new PagedResponseDto<MediaDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalResults,
                TotalPages = totalPages
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching TMDb API");
            return EmptyPage(page, pageSize);
        }
    }

    private async Task<MediaDto?> GetTmdbDetailsAsync(string externalId, string type, CancellationToken ct)
    {
        var normalizedType = type?.Trim().ToLowerInvariant() ?? "movie";
        var isTv = normalizedType is "series" or "tv";
        var endpoint = isTv ? $"/tv/{externalId}" : $"/movie/{externalId}";

        using var request = CreateTmdbRequest(HttpMethod.Get, endpoint);
        if (request == null)
        {
            return GetFallbackSampleTmdb(isTv ? "series" : "movie", 1, 1).Items.FirstOrDefault();
        }

        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            return MapTmdbItemToDto(doc.RootElement, isTv ? "series" : "movie");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching TMDb details for {Type} {Id}", type, externalId);
            return null;
        }
    }

    private async Task<PagedResponseDto<MediaDto>> GetTmdbTrendingAsync(string type, int page, int pageSize, CancellationToken ct)
    {
        var isTv = type is "series" or "tv";
        var endpoint = isTv ? $"/trending/tv/day?page={page}" : $"/trending/movie/day?page={page}";

        using var request = CreateTmdbRequest(HttpMethod.Get, endpoint);
        if (request == null)
        {
            return GetFallbackSampleTmdb(isTv ? "series" : "movie", page, pageSize);
        }

        try
        {
            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                return EmptyPage(page, pageSize);
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                return EmptyPage(page, pageSize);
            }

            var items = new List<MediaDto>();
            foreach (var el in results.EnumerateArray())
            {
                items.Add(MapTmdbItemToDto(el, isTv ? "series" : "movie"));
            }

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
            _logger.LogError(ex, "Error fetching TMDb trending");
            return EmptyPage(page, pageSize);
        }
    }

    private static MediaDto MapTmdbItemToDto(JsonElement element, string mediaType)
    {
        var id = element.GetProperty("id").GetInt64().ToString();
        var isSeries = mediaType == "series";

        string title;
        if (isSeries)
        {
            title = element.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String
                ? n.GetString() ?? "Unknown Series"
                : "Unknown Series";
        }
        else
        {
            title = element.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? "Unknown Movie"
                : "Unknown Movie";
        }

        var originalTitle = element.TryGetProperty(isSeries ? "original_name" : "original_title", out var ot) && ot.ValueKind == JsonValueKind.String
            ? ot.GetString()
            : null;

        var description = element.TryGetProperty("overview", out var ov) && ov.ValueKind == JsonValueKind.String
            ? ov.GetString()
            : null;

        string? coverImage = null;
        if (element.TryGetProperty("poster_path", out var pp) && pp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(pp.GetString()))
        {
            coverImage = $"https://image.tmdb.org/t/p/w500{pp.GetString()}";
        }

        string? bannerImage = null;
        if (element.TryGetProperty("backdrop_path", out var bp) && bp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(bp.GetString()))
        {
            bannerImage = $"https://image.tmdb.org/t/p/original{bp.GetString()}";
        }

        int? year = null;
        var dateProp = isSeries ? "first_air_date" : "release_date";
        if (element.TryGetProperty(dateProp, out var rd) && rd.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(rd.GetString()))
        {
            var str = rd.GetString()!;
            if (DateTime.TryParse(str, out var parsedDate))
            {
                year = parsedDate.Year;
            }
        }

        double? score = null;
        if (element.TryGetProperty("vote_average", out var va) && va.ValueKind == JsonValueKind.Number)
        {
            score = Math.Round(va.GetDouble(), 1);
        }

        var genres = new List<string>();
        if (element.TryGetProperty("genres", out var gnrs) && gnrs.ValueKind == JsonValueKind.Array)
        {
            foreach (var g in gnrs.EnumerateArray())
            {
                if (g.TryGetProperty("name", out var gn) && gn.ValueKind == JsonValueKind.String)
                {
                    genres.Add(gn.GetString()!);
                }
            }
        }

        int? runtimeMinutes = null;
        if (!isSeries && element.TryGetProperty("runtime", out var rt) && rt.ValueKind == JsonValueKind.Number)
        {
            runtimeMinutes = rt.GetInt32();
        }

        int? totalUnits = null;
        string? unitName = null;
        int? totalSeasons = null;

        if (isSeries)
        {
            unitName = "Episodes";
            if (element.TryGetProperty("number_of_episodes", out var ep) && ep.ValueKind == JsonValueKind.Number)
            {
                totalUnits = ep.GetInt32();
            }
            if (element.TryGetProperty("number_of_seasons", out var se) && se.ValueKind == JsonValueKind.Number)
            {
                totalSeasons = se.GetInt32();
            }
        }

        return new MediaDto
        {
            ExternalSource = "tmdb",
            ExternalId = id,
            MediaType = mediaType,
            Title = title,
            OriginalTitle = originalTitle,
            Description = description,
            CoverImage = coverImage,
            BannerImage = bannerImage,
            Year = year,
            Score = score,
            Genres = genres,
            TotalUnits = totalUnits,
            UnitName = unitName,
            TotalSeasons = totalSeasons,
            RuntimeMinutes = runtimeMinutes
        };
    }

    private static PagedResponseDto<MediaDto> GetFallbackSampleTmdb(string mediaType, int page, int pageSize)
    {
        var isSeries = mediaType is "series" or "tv";
        return new PagedResponseDto<MediaDto>
        {
            Items =
            [
                isSeries
                    ? new MediaDto
                    {
                        ExternalSource = "tmdb",
                        ExternalId = "1396",
                        MediaType = "series",
                        Title = "Breaking Bad",
                        Description = "A chemistry teacher diagnosed with inoperable lung cancer turns to manufacturing and selling methamphetamine...",
                        Year = 2008,
                        Score = 9.5,
                        TotalUnits = 62,
                        UnitName = "Episodes",
                        TotalSeasons = 5,
                        Genres = ["Drama", "Crime"]
                    }
                    : new MediaDto
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
        };
    }

    // -------------------------------------------------------------------------
    // IGDB / Twitch Integration (Games)
    // -------------------------------------------------------------------------

    private async Task<string?> GetTwitchAccessTokenAsync(CancellationToken ct)
    {
        const string cacheKey = "Twitch_OAuth_AccessToken";
        if (_memoryCache.TryGetValue(cacheKey, out string? cachedToken) && !string.IsNullOrWhiteSpace(cachedToken))
        {
            return cachedToken;
        }

        var clientId = _configuration["Igdb:ClientId"];
        var clientSecret = _configuration["Igdb:ClientSecret"];

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            _logger.LogWarning("IGDB credentials are not configured. Set 'Igdb:ClientId' and 'Igdb:ClientSecret' via dotnet user-secrets.");
            return null;
        }

        try
        {
            var requestContent = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret),
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            ]);

            var response = await _httpClient.PostAsync("https://id.twitch.tv/oauth2/token", requestContent, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("Failed to obtain Twitch OAuth token: {Status} - {Error}", response.StatusCode, err);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("access_token", out var tokenProp))
            {
                var token = tokenProp.GetString();
                var expiresInSeconds = doc.RootElement.TryGetProperty("expires_in", out var expProp) ? expProp.GetInt32() : 3600;

                // Cache token (safely minus 5 minutes before expiration)
                var cacheDuration = TimeSpan.FromSeconds(Math.Max(60, expiresInSeconds - 300));
                _memoryCache.Set(cacheKey, token, cacheDuration);

                return token;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while requesting Twitch OAuth token");
        }

        return null;
    }

    private async Task<PagedResponseDto<MediaDto>> SearchIgdbAsync(string query, int page, int pageSize, CancellationToken ct)
    {
        var token = await GetTwitchAccessTokenAsync(ct);
        var clientId = _configuration["Igdb:ClientId"];

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(clientId))
        {
            return GetFallbackSampleGame(page, pageSize);
        }

        var sanitizedQuery = query.Replace("\"", "\\\"");
        var offset = Math.Max(0, (page - 1) * pageSize);
        var apicalypseQuery = $@"
fields id, name, summary, first_release_date, total_rating, rating, cover.image_id, screenshots.image_id, genres.name, platforms.name, involved_companies.company.name, involved_companies.developer;
search ""{sanitizedQuery}"";
limit {pageSize};
offset {offset};";

        return await ExecuteIgdbQueryAsync(apicalypseQuery, clientId, token, page, pageSize, ct);
    }

    private async Task<MediaDto?> GetIgdbDetailsAsync(string externalId, CancellationToken ct)
    {
        var token = await GetTwitchAccessTokenAsync(ct);
        var clientId = _configuration["Igdb:ClientId"];

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(clientId))
        {
            return GetFallbackSampleGame(1, 1).Items.FirstOrDefault();
        }

        var apicalypseQuery = $@"
fields id, name, summary, first_release_date, total_rating, rating, cover.image_id, screenshots.image_id, genres.name, platforms.name, involved_companies.company.name, involved_companies.developer;
where id = {externalId};
limit 1;";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games");
            request.Headers.Add("Client-ID", clientId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(apicalypseQuery, Encoding.UTF8, "text/plain");

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
            {
                return MapIgdbElementToDto(doc.RootElement[0]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching IGDB details for ID {ExternalId}", externalId);
        }

        return null;
    }

    private async Task<PagedResponseDto<MediaDto>> GetIgdbTrendingAsync(int page, int pageSize, CancellationToken ct)
    {
        var token = await GetTwitchAccessTokenAsync(ct);
        var clientId = _configuration["Igdb:ClientId"];

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(clientId))
        {
            return GetFallbackSampleGame(page, pageSize);
        }

        var offset = Math.Max(0, (page - 1) * pageSize);
        var apicalypseQuery = $@"
fields id, name, summary, first_release_date, total_rating, rating, cover.image_id, screenshots.image_id, genres.name, platforms.name, involved_companies.company.name, involved_companies.developer;
where total_rating_count > 25 & first_release_date != null;
sort total_rating_count desc;
limit {pageSize};
offset {offset};";

        return await ExecuteIgdbQueryAsync(apicalypseQuery, clientId, token, page, pageSize, ct);
    }

    private async Task<PagedResponseDto<MediaDto>> ExecuteIgdbQueryAsync(
        string apicalypseQuery, string clientId, string token, int page, int pageSize, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games");
            request.Headers.Add("Client-ID", clientId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(apicalypseQuery, Encoding.UTF8, "text/plain");

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("IGDB API returned status {Status}: {Error}", response.StatusCode, err);
                return EmptyPage(page, pageSize);
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return EmptyPage(page, pageSize);
            }

            var items = new List<MediaDto>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                items.Add(MapIgdbElementToDto(el));
            }

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
            _logger.LogError(ex, "Error querying IGDB games API");
            return EmptyPage(page, pageSize);
        }
    }

    private static MediaDto MapIgdbElementToDto(JsonElement element)
    {
        var id = element.GetProperty("id").GetInt64().ToString();
        var name = element.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "Unknown Game" : "Unknown Game";
        var summary = element.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null;

        string? coverImage = null;
        if (element.TryGetProperty("cover", out var cov) && cov.ValueKind == JsonValueKind.Object &&
            cov.TryGetProperty("image_id", out var covImg) && covImg.ValueKind == JsonValueKind.String)
        {
            coverImage = $"https://images.igdb.com/igdb/image/upload/t_cover_big/{covImg.GetString()}.jpg";
        }

        string? bannerImage = null;
        if (element.TryGetProperty("screenshots", out var sc) && sc.ValueKind == JsonValueKind.Array && sc.GetArrayLength() > 0)
        {
            var firstSc = sc.EnumerateArray().First();
            if (firstSc.TryGetProperty("image_id", out var scImg) && scImg.ValueKind == JsonValueKind.String)
            {
                bannerImage = $"https://images.igdb.com/igdb/image/upload/t_screenshot_big/{scImg.GetString()}.jpg";
            }
        }

        int? year = null;
        if (element.TryGetProperty("first_release_date", out var rd) && rd.ValueKind == JsonValueKind.Number)
        {
            year = DateTimeOffset.FromUnixTimeSeconds(rd.GetInt64()).Year;
        }

        double? score = null;
        if (element.TryGetProperty("total_rating", out var tr) && tr.ValueKind == JsonValueKind.Number)
        {
            score = Math.Round(tr.GetDouble() / 10.0, 1);
        }
        else if (element.TryGetProperty("rating", out var r) && r.ValueKind == JsonValueKind.Number)
        {
            score = Math.Round(r.GetDouble() / 10.0, 1);
        }

        var genres = new List<string>();
        if (element.TryGetProperty("genres", out var gnrs) && gnrs.ValueKind == JsonValueKind.Array)
        {
            foreach (var g in gnrs.EnumerateArray())
            {
                if (g.TryGetProperty("name", out var gn) && gn.ValueKind == JsonValueKind.String)
                {
                    genres.Add(gn.GetString()!);
                }
            }
        }

        var platforms = new List<string>();
        if (element.TryGetProperty("platforms", out var plats) && plats.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in plats.EnumerateArray())
            {
                if (p.TryGetProperty("name", out var pn) && pn.ValueKind == JsonValueKind.String)
                {
                    platforms.Add(pn.GetString()!);
                }
            }
        }

        string? developer = null;
        if (element.TryGetProperty("involved_companies", out var comps) && comps.ValueKind == JsonValueKind.Array)
        {
            foreach (var comp in comps.EnumerateArray())
            {
                var isDev = comp.TryGetProperty("developer", out var d) && d.ValueKind == JsonValueKind.True;
                if (isDev && comp.TryGetProperty("company", out var c) && c.TryGetProperty("name", out var cn) && cn.ValueKind == JsonValueKind.String)
                {
                    developer = cn.GetString();
                    break;
                }
            }
        }

        return new MediaDto
        {
            ExternalSource = "igdb",
            ExternalId = id,
            MediaType = "game",
            Title = name,
            Description = summary,
            CoverImage = coverImage,
            BannerImage = bannerImage,
            Year = year,
            Score = score,
            Genres = genres,
            Platforms = platforms,
            Developer = developer
        };
    }

    private static PagedResponseDto<MediaDto> GetFallbackSampleGame(int page, int pageSize) =>
        new()
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
        };

    private static PagedResponseDto<MediaDto> EmptyPage(int page, int pageSize) =>
        new() { Items = [], Page = page, PageSize = pageSize, TotalItems = 0, TotalPages = 0 };
}
