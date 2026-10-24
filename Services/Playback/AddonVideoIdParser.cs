namespace Kue.Api.Services.Playback;

/// <summary>
/// A validated Stremio video id. Both id shapes observed in the protocol are
/// supported: <c>tt1234567</c>, <c>tt1234567:2:5</c>, <c>tmdb:603</c> and
/// <c>tmdb:1399:2:5</c>.
/// </summary>
public sealed record AddonVideoId(
    string MediaType,
    string ExternalSource,
    string ExternalId,
    int? Season,
    int? Episode)
{
    /// <summary>
    /// Stable key used for debounce lookups. Season/episode are normalised to
    /// -1 so they can participate in an EF query without nullable comparison
    /// surprises.
    /// </summary>
    public (string MediaType, string ExternalSource, string ExternalId, int Season, int Episode) Key =>
        (MediaType, ExternalSource, ExternalId, Season ?? -1, Episode ?? -1);
}

public static class AddonVideoIdParser
{
    private const int MaxRawLength = 100;
    private const int MaxDigits = 12;

    /// <summary>
    /// Parses a Stremio <c>(type, id)</c> pair. Returns null for anything the
    /// addon should not record (unknown media type, malformed id, episode
    /// coordinates on a movie, out-of-range season/episode, etc.).
    /// </summary>
    public static AddonVideoId? TryParse(string? type, string? rawId)
    {
        var mediaType = NormalizeType(type);
        if (mediaType is null) return null;

        var value = CleanId(rawId);
        if (value is null) return null;

        var parts = value.Split(':');
        if (parts.Length is 0 or > 4) return null;

        var prefix = parts[0].Trim().ToLowerInvariant();

        string externalSource;
        string externalId;

        if (prefix == "tmdb")
        {
            // tmdb:<id> | tmdb:<id>:<season>:<episode>
            if (parts.Length is not (2 or 4)) return null;
            if (!IsDigits(parts[1])) return null;
            externalSource = "tmdb";
            externalId = parts[1];
        }
        else if (prefix.StartsWith("tt", StringComparison.Ordinal))
        {
            // tt<digits> | tt<digits>:<season>:<episode>
            if (parts.Length is not (1 or 3)) return null;
            if (!IsDigits(prefix[2..])) return null;
            externalSource = "imdb";
            externalId = prefix;
        }
        else
        {
            return null;
        }

        int? season = null;
        int? episode = null;

        if (parts.Length is 3 or 4)
        {
            // Both supported shapes put season/episode last.
            var seasonPart = parts[^2];
            var episodePart = parts[^1];

            if (!TryParseNumber(seasonPart, 0, 999, out var parsedSeason)) return null;
            if (!TryParseNumber(episodePart, 1, 9999, out var parsedEpisode)) return null;

            season = parsedSeason;
            episode = parsedEpisode;

            if (mediaType == "movie") return null;
        }

        return new AddonVideoId(mediaType, externalSource, externalId, season, episode);
    }

    private static string? NormalizeType(string? type)
    {
        var value = type?.Trim().ToLowerInvariant();
        return value switch
        {
            "movie" or "film" => "movie",
            "series" or "tv" or "tvshow" or "tv-show" or "show" => "series",
            _ => null
        };
    }

    private static string? CleanId(string? rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId)) return null;

        var value = rawId.Trim();
        if (value.Length > MaxRawLength) return null;

        // Nuvio appends videoHash/videoSize as extra path segments and may
        // append them after a query string. Only the first segment is the id.
        var cut = value.IndexOfAny(['/', '?', '&']);
        if (cut >= 0) value = value[..cut];

        if (value.Length == 0) return null;

        if (value.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..^5];
        }

        return value.Length == 0 ? null : value;
    }

    private static bool IsDigits(string value)
    {
        if (value.Length is 0 or > MaxDigits) return false;
        foreach (var c in value)
        {
            if (c is < '0' or > '9') return false;
        }
        return true;
    }

    private static bool TryParseNumber(string value, int min, int max, out int parsed)
    {
        parsed = 0;
        if (!IsDigits(value)) return false;
        if (!int.TryParse(value, out parsed)) return false;
        return parsed >= min && parsed <= max;
    }
}
