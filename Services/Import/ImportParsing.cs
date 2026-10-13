using System.Text.Json;
using System.Xml.Linq;

namespace Kue.Api.Services.Import;

/// <summary>One normalized list entry, regardless of source format.</summary>
public class ParsedEntry
{
    public string Title { get; set; } = string.Empty;
    public string? MediaType { get; set; }
    public string? ExternalSource { get; set; }
    public string? ExternalId { get; set; }
    public string? Status { get; set; }
    public int? Progress { get; set; }
    public int? Rating { get; set; }
    public string? Platform { get; set; }
    public bool? IsFavorite { get; set; }
    public int? Year { get; set; }
}

public static class ImportParsing
{
    private static readonly HashSet<string> ValidStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "planning", "in_progress", "completed", "on_hold", "dropped"
    };

    private static readonly Dictionary<string, string> StatusMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["completed"] = "completed", ["complete"] = "completed", ["watched"] = "completed",
        ["read"] = "completed", ["finished"] = "completed",
        ["watching"] = "in_progress", ["reading"] = "in_progress", ["playing"] = "in_progress",
        ["current"] = "in_progress", ["in_progress"] = "in_progress", ["in progress"] = "in_progress",
        ["planning"] = "planning", ["plan to watch"] = "planning", ["plan to read"] = "planning",
        ["plan_to_watch"] = "planning", ["plan"] = "planning", ["watchlist"] = "planning",
        ["on_hold"] = "on_hold", ["on hold"] = "on_hold", ["paused"] = "on_hold",
        ["dropped"] = "dropped",
    };

    public static string? NormalizeStatus(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return StatusMap.TryGetValue(value.Trim(), out var mapped) ? mapped : null;
    }

    public static bool IsValidStatus(string? value) =>
        !string.IsNullOrWhiteSpace(value) && ValidStatuses.Contains(value.Trim());

    /// <summary>Detects mal-xml, anilist-json, trakt-json or csv.</summary>
    public static string SniffFormat(string fileName, string content)
    {
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith("<myanimelist", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("<myinfo", StringComparison.OrdinalIgnoreCase))
        {
            return "mal-xml";
        }
        if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
        {
            try
            {
                using var doc = JsonDocument.Parse(trimmed);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("data", out var data)
                        && data.ValueKind == JsonValueKind.Object
                        && data.TryGetProperty("MediaListCollection", out _))
                    {
                        return "anilist-json";
                    }
                    if (root.TryGetProperty("movies", out _) || root.TryGetProperty("shows", out _))
                    {
                        return "trakt-json";
                    }
                    if (root.TryGetProperty("trakt", out _)
                        || ((root.TryGetProperty("movie", out _) || root.TryGetProperty("show", out _))
                            && root.TryGetProperty("ids", out _)))
                    {
                        return "trakt-json";
                    }
                }
                else if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
                {
                    var first = root[0];
                    if (first.ValueKind == JsonValueKind.Object
                        && (first.TryGetProperty("movie", out _) || first.TryGetProperty("show", out _)))
                    {
                        return "trakt-json";
                    }
                }
            }
            catch (JsonException) { }
        }
        if (fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return "mal-xml";
        if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return "anilist-json";
        return "csv";
    }

    public static List<ParsedEntry> Parse(
        string format, string content, Dictionary<string, string>? columnMapping, string? defaultType)
    {
        return format switch
        {
            "mal-xml" => ParseMalXml(content),
            "anilist-json" => ParseAniListJson(content),
            "trakt-json" => ParseTraktJson(content),
            _ => ParseCsv(content, columnMapping, defaultType),
        };
    }

    // --- MyAnimeList XML ---

    private static List<ParsedEntry> ParseMalXml(string content)
    {
        var entries = new List<ParsedEntry>();
        var doc = XDocument.Parse(content);

        foreach (var section in new[] { "anime", "manga" })
        {
            var mediaType = section == "anime" ? "anime" : "manga";
            foreach (var el in doc.Descendants(section))
            {
                var title = (string?)el.Element("series_title") ?? string.Empty;
                if (string.IsNullOrWhiteSpace(title)) continue;

                int? progress = section == "anime"
                    ? ParseInt((string?)el.Element("my_watched_episodes"))
                    : ParseInt((string?)el.Element("my_read_chapters"));

                entries.Add(new ParsedEntry
                {
                    Title = title.Trim(),
                    MediaType = mediaType,
                    ExternalSource = "mal",
                    ExternalId = ((string?)el.Element("series_animedb_id"))?.Trim(),
                    Status = NormalizeStatus((string?)el.Element("my_status")) ?? "planning",
                    Progress = progress,
                    Rating = ParseInt((string?)el.Element("my_score")) is int s && s > 0 ? s : null,
                });
            }
        }

        return entries;
    }

    // --- AniList JSON export ---

    private static List<ParsedEntry> ParseAniListJson(string content)
    {
        using var doc = JsonDocument.Parse(content);
        if (!doc.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("MediaListCollection", out var collection)
            || !collection.TryGetProperty("lists", out var lists))
        {
            return [];
        }

        var raw = new List<(ParsedEntry Entry, int RawScore)>();
        foreach (var list in lists.EnumerateArray())
        {
            if (!list.TryGetProperty("entries", out var items)) continue;
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("media", out var media)) continue;

                var title = "";
                if (media.TryGetProperty("title", out var titleEl))
                {
                    title = GetString(titleEl, "english") ?? GetString(titleEl, "romaji") ?? "";
                }
                if (string.IsNullOrWhiteSpace(title)) continue;

                var format = GetString(media, "format") ?? "";
                var mediaType = format.StartsWith("MANGA", StringComparison.OrdinalIgnoreCase)
                    || format.StartsWith("NOVEL", StringComparison.OrdinalIgnoreCase)
                    || format.StartsWith("ONE_SHOT", StringComparison.OrdinalIgnoreCase)
                    ? "manga" : "anime";

                var status = GetString(item, "status") switch
                {
                    "CURRENT" => "in_progress",
                    "PLANNING" => "planning",
                    "COMPLETED" => "completed",
                    "DROPPED" => "dropped",
                    "PAUSED" => "on_hold",
                    "REPEATING" => "completed",
                    _ => "planning",
                };

                var entry = new ParsedEntry
                {
                    Title = title.Trim(),
                    MediaType = mediaType,
                    Status = status,
                    Progress = GetInt(item, "progress"),
                    Year = media.TryGetProperty("startDate", out var sd) ? GetInt(sd, "year") : null,
                };
                if (media.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
                {
                    entry.ExternalSource = "anilist";
                    entry.ExternalId = id.GetInt32().ToString();
                }

                raw.Add((entry, GetInt(item, "score") ?? 0));
            }
        }

        // The export doesn't carry the user's score format; infer the scale
        // from the file maximum (100-point vs 10-point).
        var maxScore = raw.Count > 0 ? raw.Max(x => x.RawScore) : 0;
        foreach (var (entry, rawScore) in raw)
        {
            if (rawScore <= 0) continue;
            entry.Rating = maxScore > 10 ? Math.Max(1, (int)Math.Round(rawScore / 10.0)) : rawScore;
        }

        return raw.Select(x => x.Entry).ToList();
    }

    // --- Trakt JSON export (single file + multi-file full export) ---

    /// <summary>
    /// Trakt VIP backups ship as ~44 loose .json files (watched-*, ratings-*,
    /// collection-*, lists-*, ...). Most are empty "[]" for a given user; any
    /// file with recognizable movie/show nodes is merged. Files that only
    /// carry social/metadata (user-*, network-*, comments-*, notes-*, likes-*,
    /// hidden-*) are skipped. Entries dedupe by external id, then title+year.
    /// Status priority: completed > in_progress > planning. Collection means
    /// "owns it", not "watched it", so it maps to planning and lets a watched
    /// entry upgrade it to completed.
    /// </summary>
    public static List<ParsedEntry> ParseTraktFiles(IEnumerable<(string FileName, string Content)> files)
    {
        var merged = new Dictionary<string, ParsedEntry>(StringComparer.OrdinalIgnoreCase);
        var titleIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var episodeCounts = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (rawName, content) in files)
        {
            var fileName = (rawName ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(content)) continue;
            var trimmed = content.Trim();
            if (trimmed is "[]" or "{}" or "") continue;

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(content);
            }
            catch (JsonException)
            {
                // One bad file must not sink the other 43.
                continue;
            }

            using (doc)
            {
                var kind = ClassifyTraktFile(fileName);
                if (kind == TraktFileKind.Ignore) continue;
                foreach (var raw in ExtractTraktMedia(doc.RootElement, kind))
                {
                    MergeTraktEntry(merged, titleIndex, episodeCounts, raw, kind);
                }
            }
        }

        return merged.Values.ToList();
    }

    private enum TraktFileKind
    {
        Ignore,
        Watched,
        Ratings,
        Collection,
        Watchlist,
        Favorites,
        Playback,
        CustomList,
        Generic,
    }

    private static TraktFileKind ClassifyTraktFile(string fileName)
    {
        if (fileName.Contains("user-") || fileName.Contains("network-")
            || fileName.Contains("comments-") || fileName.Contains("notes-")
            || fileName.Contains("likes-") || fileName.Contains("hidden-"))
        {
            return TraktFileKind.Ignore;
        }
        if (fileName.Contains("ratings-")) return TraktFileKind.Ratings;
        if (fileName.Contains("watched-playback")) return TraktFileKind.Playback;
        if (fileName.Contains("watched-history") || fileName is "watched-movies.json" or "watched-shows.json"
            || (fileName.Contains("watched-") && !fileName.Contains("lists-"))) return TraktFileKind.Watched;
        if (fileName.Contains("collection-")) return TraktFileKind.Collection;
        if (fileName.Contains("watchlist")) return TraktFileKind.Watchlist;
        if (fileName.Contains("favorites") || fileName.Contains("favourites")) return TraktFileKind.Favorites;
        if (fileName.Contains("lists-")) return TraktFileKind.CustomList;
        return TraktFileKind.Generic;
    }

    /// <summary>True when any filename looks like a Trakt backup file.</summary>
    public static bool LooksLikeTraktExport(IEnumerable<string> fileNames)
    {
        foreach (var raw in fileNames)
        {
            var name = (raw ?? string.Empty).Trim().ToLowerInvariant();
            if (name is "watched-movies.json" or "watched-shows.json"
                || name.Contains("watched-history") || name.Contains("watched-playback")
                || name.Contains("ratings-") || name.Contains("collection-")
                || name.Contains("lists-watchlist") || name.Contains("lists-favorites"))
            {
                return true;
            }
        }
        return false;
    }

    private sealed class RawTraktMedia
    {
        public string Title { get; set; } = string.Empty;
        public string MediaType { get; set; } = "movie";
        public string? ExternalSource { get; set; }
        public string? ExternalId { get; set; }
        public int? Rating { get; set; }
        public int? Year { get; set; }
        public int? Progress { get; set; }
        public string? EpisodeKey { get; set; }
        /// <summary>Legacy single-file sections ({movies, watchlist}) carry their own status.</summary>
        public string? StatusOverride { get; set; }
    }

    private static IEnumerable<RawTraktMedia> ExtractTraktMedia(JsonElement root, TraktFileKind kind)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in root.EnumerateArray())
            {
                var raw = ExtractTraktNode(node);
                if (raw is null) continue;
                // Bare history-style arrays predate per-file kinds: they mean watched.
                if (kind == TraktFileKind.Generic) raw.StatusOverride = "completed";
                yield return raw;
            }
            yield break;
        }

        if (root.ValueKind != JsonValueKind.Object) yield break;

        // Legacy single-file shape: { movies: [...], shows: [...], watchlist: [...] }
        foreach (var (prop, status) in new[] { ("movies", "completed"), ("shows", "completed"), ("watchlist", "planning"), ("items", (string?)null) })
        {
            if (root.TryGetProperty(prop, out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var node in arr.EnumerateArray())
                {
                    var raw = ExtractTraktNode(node);
                    if (raw is null) continue;
                    raw.StatusOverride = status;
                    yield return raw;
                }
            }
        }

        // Custom-list shape: { name, items: [...] } is covered above via "items".
        // Single object: { movie/show: {...} }
        if (root.TryGetProperty("movie", out _) || root.TryGetProperty("show", out _))
        {
            var raw = ExtractTraktNode(root);
            if (raw is not null) yield return raw;
        }
    }

    /// <summary>
    /// Pulls the movie/show out of one Trakt wrapper object. Season/episode
    /// rows roll up to the parent show; history rows carry a per-episode key
    /// so re-watches don't inflate the episode count.
    /// </summary>
    private static RawTraktMedia? ExtractTraktNode(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object) return null;

        // History items tag their kind: { type: "movie"|"episode"|"show"|"season", ... }
        var typeHint = GetString(node, "type")?.ToLowerInvariant();

        JsonElement mediaNode = default;
        string mediaType = "movie";
        var hasMedia = false;

        if (typeHint == "movie" && node.TryGetProperty("movie", out var tm))
        {
            mediaNode = tm; mediaType = "movie"; hasMedia = true;
        }
        else if ((typeHint is "episode" or "show" or "season") && node.TryGetProperty("show", out var ts))
        {
            mediaNode = ts; mediaType = "series"; hasMedia = true;
        }
        else if (node.TryGetProperty("movie", out var movie))
        {
            mediaNode = movie; mediaType = "movie"; hasMedia = true;
        }
        else if (node.TryGetProperty("show", out var show))
        {
            mediaNode = show; mediaType = "series"; hasMedia = true;
        }
        else if (node.TryGetProperty("title", out _) && node.TryGetProperty("ids", out _))
        {
            // Bare media object (no wrapper).
            mediaNode = node; hasMedia = true;
        }

        if (!hasMedia || mediaNode.ValueKind != JsonValueKind.Object) return null;

        var title = GetString(mediaNode, "title") ?? "";
        if (string.IsNullOrWhiteSpace(title)) return null;

        var (extSource, extId) = BestTraktId(mediaNode);

        // Rating lives on the wrapper (ratings-*.json), not inside the media node.
        var rating = GetInt(node, "rating") ?? GetInt(mediaNode, "rating");
        if (rating is < 1 or > 10) rating = null;

        int? progress = null;
        string? episodeKey = null;
        if (node.TryGetProperty("episode", out var ep) && ep.ValueKind == JsonValueKind.Object)
        {
            var season = GetInt(ep, "season") ?? GetInt(node, "season");
            var number = GetInt(ep, "number");
            if (season.HasValue && number.HasValue) episodeKey = $"s{season}e{number}";
        }

        // Playback rows carry a 0-100 watch percent on the wrapper.
        if (node.TryGetProperty("progress", out var prog) && prog.ValueKind == JsonValueKind.Number)
        {
            try
            {
                var percent = prog.TryGetInt32(out var pi) ? pi : (int)Math.Round(prog.GetDouble());
                if (percent is >= 0 and <= 100) progress = percent;
            }
            catch (InvalidOperationException) { }
        }

        return new RawTraktMedia
        {
            Title = title.Trim(),
            MediaType = mediaType,
            ExternalSource = extSource,
            ExternalId = extId,
            Rating = rating,
            Year = GetInt(mediaNode, "year"),
            Progress = progress,
            EpisodeKey = episodeKey,
        };
    }

    /// <summary>Prefer tmdb (matches the local catalog), then trakt, then imdb.</summary>
    private static (string? Source, string? Id) BestTraktId(JsonElement mediaNode)
    {
        if (mediaNode.TryGetProperty("ids", out var ids) && ids.ValueKind == JsonValueKind.Object)
        {
            foreach (var source in new[] { "tmdb", "trakt", "imdb" })
            {
                if (!ids.TryGetProperty(source, out var prop)) continue;
                if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var num) && num > 0)
                {
                    return (source, num.ToString());
                }
                if (prop.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(prop.GetString()))
                {
                    return (source, prop.GetString()!.Trim());
                }
            }
        }
        return (null, null);
    }

    private static int StatusRank(string? status) => status switch
    {
        "completed" => 3,
        "in_progress" => 2,
        _ => 1,
    };

    private static int SourceRank(string? source) => source switch
    {
        "tmdb" => 3,
        "trakt" => 2,
        "imdb" => 1,
        _ => 0,
    };

    private static void MergeTraktEntry(
        Dictionary<string, ParsedEntry> merged,
        Dictionary<string, string> titleIndex,
        Dictionary<string, HashSet<string>> episodeCounts,
        RawTraktMedia raw,
        TraktFileKind kind)
    {
        var status = raw.StatusOverride ?? kind switch
        {
            TraktFileKind.Watched or TraktFileKind.Ratings => "completed",
            TraktFileKind.Playback => "in_progress",
            _ => "planning",
        };
        var favorite = kind == TraktFileKind.Favorites ? true : (bool?)null;

        var key = raw.ExternalSource is not null && raw.ExternalId is not null
            ? $"{raw.ExternalSource}:{raw.ExternalId}".ToLowerInvariant()
            : $"title:{raw.MediaType}:{raw.Title.ToLowerInvariant()}:{raw.Year}";

        // Same title seen first without ids, now with ids: migrate the entry.
        var titleKey = $"title:{raw.MediaType}:{raw.Title.ToLowerInvariant()}:{raw.Year}";
        if (key != titleKey && titleIndex.TryGetValue(titleKey, out var oldKey)
            && merged.TryGetValue(oldKey, out var oldEntry)
            && oldEntry.ExternalId is null)
        {
            merged.Remove(oldKey);
            merged[key] = oldEntry;
            titleIndex[titleKey] = key;
        }

        if (!merged.TryGetValue(key, out var entry))
        {
            entry = new ParsedEntry
            {
                Title = raw.Title,
                MediaType = raw.MediaType,
                ExternalSource = raw.ExternalSource,
                ExternalId = raw.ExternalId,
                Status = status,
                Year = raw.Year,
            };
            merged[key] = entry;
            titleIndex.TryAdd(titleKey, key);
        }

        if (StatusRank(status) >= StatusRank(entry.Status)) entry.Status = status;
        if (raw.Rating.HasValue)
        {
            entry.Rating = entry.Rating.HasValue ? Math.Max(entry.Rating.Value, raw.Rating.Value) : raw.Rating;
        }
        if (favorite.HasValue) entry.IsFavorite = true;
        // Playback percent is 0-100 and would mix units with the episode-count
        // progress used for series, so only keep it for movies.
        if (kind == TraktFileKind.Playback && raw.MediaType == "movie" && raw.Progress.HasValue)
        {
            var progress = raw.Progress.Value;
            entry.Progress = entry.Progress.HasValue ? Math.Max(entry.Progress.Value, progress) : progress;
        }
        if (entry.Year is null) entry.Year = raw.Year;
        if (SourceRank(raw.ExternalSource) > SourceRank(entry.ExternalSource))
        {
            entry.ExternalSource = raw.ExternalSource;
            entry.ExternalId = raw.ExternalId;
        }

        // Series episode accounting: distinct episodes only, so history
        // re-watches across history-1/history-2 don't inflate progress.
        if (raw.MediaType == "series" && raw.EpisodeKey is not null)
        {
            if (!episodeCounts.TryGetValue(key, out var seen))
            {
                seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                episodeCounts[key] = seen;
            }
            if (seen.Add(raw.EpisodeKey))
            {
                entry.Progress = seen.Count;
            }
        }
        else if (raw.MediaType == "series" && kind == TraktFileKind.Watched)
        {
            // watched-shows rows without per-episode detail still count as one.
            entry.Progress = Math.Max(entry.Progress ?? 0, 1);
        }
    }

    private static List<ParsedEntry> ParseTraktJson(string content)
    {
        // Single-file back-compat: route through the multi-file merger so
        // ratings on the wrapper and id fallback behave the same.
        return ParseTraktFiles([(string.Empty, content)]);
    }

    // --- Generic CSV ---

    private static List<ParsedEntry> ParseCsv(
        string content, Dictionary<string, string>? columnMapping, string? defaultType)
    {
        var rows = SplitCsvRows(content);
        if (rows.Count < 2) return [];

        var headers = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        var map = ResolveColumns(headers, columnMapping);
        if (!map.TryGetValue("title", out var titleIdx)) return [];

        var entries = new List<ParsedEntry>();
        foreach (var row in rows.Skip(1))
        {
            string Cell(string key) =>
                map.TryGetValue(key, out var idx) && idx < row.Count ? row[idx].Trim() : "";

            var title = Cell("title");
            if (string.IsNullOrWhiteSpace(title)) continue;

            var status = NormalizeStatus(Cell("status")) ?? "planning";

            int? number(string key)
            {
                var raw = Cell(key);
                if (string.IsNullOrWhiteSpace(raw)) return null;
                // "12/24", "8.5", "7 hrs" all reduce to their leading number.
                var digits = new string(raw.Trim().TakeWhile(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
                if (double.TryParse(digits, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var value)
                    && value >= 0)
                {
                    return (int)Math.Round(value);
                }
                return null;
            }

            var score = number("score");
            if (score.HasValue && score.Value > 10)
            {
                score = Math.Max(1, (int)Math.Round(score.Value / 10.0));
            }

            entries.Add(new ParsedEntry
            {
                Title = title,
                MediaType = NormalizeDbType(Cell("type")) ?? NormalizeDbType(defaultType),
                Status = status,
                Progress = number("progress"),
                Rating = score is >= 1 and <= 10 ? score : null,
                Platform = string.IsNullOrWhiteSpace(Cell("platform")) ? null : Cell("platform"),
                IsFavorite = Cell("favorite").ToLowerInvariant() is "true" or "1" or "yes" ? true : null,
            });
        }

        return entries;
    }

    private static Dictionary<string, int> ResolveColumns(
        List<string> headers, Dictionary<string, string>? explicitMapping)
    {
        var resolved = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (explicitMapping is not null)
        {
            foreach (var (key, column) in explicitMapping)
            {
                var idx = headers.FindIndex(h => h == column.Trim().ToLowerInvariant());
                if (idx >= 0) resolved[key] = idx;
            }
            return resolved;
        }

        string?[] titleKeys = ["title", "name", "series_title", "show", "game", "book"];
        string?[] statusKeys = ["status", "state", "my_status", "mylist_status", "list_status"];
        string?[] scoreKeys = ["score", "rating", "my_score", "my_rating"];
        string?[] progressKeys = ["progress", "episodes", "my_watched_episodes", "watched", "chapters",
            "my_read_chapters", "hours", "playtime", "current", "ep", "ch"];
        string?[] typeKeys = ["type", "media_type", "format", "media"];
        string?[] platformKeys = ["platform", "platforms"];

        int Find(string?[] keys)
        {
            foreach (var key in keys)
            {
                var idx = headers.FindIndex(h => h == key);
                if (idx >= 0) return idx;
            }
            return -1;
        }

        var title = Find(titleKeys);
        if (title >= 0) resolved["title"] = title;
        var status = Find(statusKeys);
        if (status >= 0) resolved["status"] = status;
        var score = Find(scoreKeys);
        if (score >= 0) resolved["score"] = score;
        var progress = Find(progressKeys);
        if (progress >= 0) resolved["progress"] = progress;
        var type = Find(typeKeys);
        if (type >= 0) resolved["type"] = type;
        var platform = Find(platformKeys);
        if (platform >= 0) resolved["platform"] = platform;
        var fav = headers.FindIndex(h => h is "favorite" or "favourite" or "is_favorite");
        if (fav >= 0) resolved["favorite"] = fav;

        return resolved;
    }

    private static string? NormalizeDbType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return value.Trim().ToLowerInvariant() switch
        {
            "tv" or "tvshow" or "tv-show" or "show" or "shows" or "series" => "series",
            "film" or "films" or "movies" or "movie" => "movie",
            "games" or "videogame" or "video-game" or "game" => "game",
            "manga" or "mangas" or "novel" or "light novel" => "manga",
            "anime" or "animes" => "anime",
            _ => null,
        };
    }

    private static List<List<string>> SplitCsvRows(string content)
    {
        var rows = new List<List<string>>();
        var current = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < content.Length; i++)
        {
            var c = content[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < content.Length && content[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                current.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\r')
            {
                continue;
            }
            else if (c == '\n')
            {
                current.Add(field.ToString());
                field.Clear();
                if (current.Count > 1 || (current.Count == 1 && !string.IsNullOrWhiteSpace(current[0])))
                {
                    rows.Add(current);
                }
                current = [];
            }
            else
            {
                field.Append(c);
            }
        }

        current.Add(field.ToString());
        if (current.Count > 1 || (current.Count == 1 && !string.IsNullOrWhiteSpace(current[0])))
        {
            rows.Add(current);
        }
        return rows;
    }

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return int.TryParse(value.Trim(), out var parsed) ? parsed : null;
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        return element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static int? GetInt(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return null;
        return element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number
            ? prop.GetInt32()
            : null;
    }
}
