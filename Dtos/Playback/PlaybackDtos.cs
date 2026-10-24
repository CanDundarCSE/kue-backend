namespace Kue.Api.Dtos.Playback;

/// <summary>
/// A playback-start candidate. This is deliberately not a library entry:
/// <see cref="MediaId"/> may be null, and no watch progress exists here.
/// </summary>
public class PlaybackCandidateDto
{
    public int Id { get; set; }
    public int? MediaId { get; set; }
    public bool IsResolved => MediaId.HasValue;

    public string MediaType { get; set; } = null!;
    public string ExternalSource { get; set; } = null!;
    public string ExternalId { get; set; } = null!;

    public string? Title { get; set; }
    public string? CoverImage { get; set; }
    public int? Year { get; set; }
    public int? TotalUnits { get; set; }
    public string? UnitName { get; set; }

    public int? Season { get; set; }
    public int? Episode { get; set; }

    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
    public int RequestCount { get; set; }
}

public class AddonCredentialDto
{
    public bool Configured { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public class AddonCredentialCreatedDto
{
    /// <summary>Raw token. Returned exactly once; Kue only stores its hash.</summary>
    public string Token { get; set; } = null!;
    public string ManifestUrl { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
