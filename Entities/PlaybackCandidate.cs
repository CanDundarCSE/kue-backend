namespace Kue.Api.Entities;

/// <summary>
/// Evidence that a Stremio-protocol client asked our addon for subtitles for
/// one item. This is a *candidate* only: it records that playback was probably
/// initialised, never that the item was watched or completed. Only an explicit
/// user confirmation turns a candidate into a library entry / watch history.
/// </summary>
public class PlaybackCandidate
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>
    /// Resolved Kue media. Null when the external id could not be mapped (for
    /// example IMDb-to-TMDB lookup failed); the raw identifiers are kept so the
    /// candidate can still be inspected.
    /// </summary>
    public int? MediaId { get; set; }
    public Media? Media { get; set; }

    /// <summary>Canonical Stremio type from the request: "movie" or "series".</summary>
    public string MediaType { get; set; } = null!;

    /// <summary>Id space the client used: "imdb" or "tmdb".</summary>
    public string ExternalSource { get; set; } = null!;

    /// <summary>Id exactly as sent by the client (e.g. "tt26657236" or "1399").</summary>
    public string ExternalId { get; set; } = null!;

    /// <summary>TMDB id the request was mapped to, when resolution succeeded.</summary>
    public string? ResolvedExternalId { get; set; }

    public int? Season { get; set; }
    public int? Episode { get; set; }

    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    /// <summary>Number of requests folded into this candidate by the debounce window.</summary>
    public int RequestCount { get; set; } = 1;

    public string Status { get; set; } = PlaybackCandidateStatus.Pending;
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? DismissedAt { get; set; }
}

public static class PlaybackCandidateStatus
{
    public const string Pending = "pending";
    public const string Confirmed = "confirmed";
    public const string Dismissed = "dismissed";
}
