namespace Kue.Api.Dtos.Playback;

public class AddonCredentialSummaryDto
{
    public int Id { get; set; }
    public string? Label { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public class AddonCredentialListDto
{
    public bool Configured { get; set; }
    public IReadOnlyList<AddonCredentialSummaryDto> Credentials { get; set; } = [];
}

public class AddonCredentialCreateRequest
{
    /// <summary>Optional label so devices can be told apart ("Windows PC").</summary>
    public string? Label { get; set; }
}

public class AddonCredentialCreatedDto
{
    /// <summary>Raw token. Returned exactly once; Kue only stores its hash.</summary>
    public string Token { get; set; } = null!;
    public string ManifestUrl { get; set; } = null!;
    public int Id { get; set; }
    public string? Label { get; set; }
    public DateTime CreatedAt { get; set; }
}
