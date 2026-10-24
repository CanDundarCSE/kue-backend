namespace Kue.Api.Dtos.Playback;

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
