namespace Kue.Api.Entities;

/// <summary>
/// A per-user credential for the Stremio-compatible addon endpoint. The raw
/// token lives only in the manifest URL handed to the user at creation time;
/// this row stores a SHA-256 hash so a database leak cannot be replayed.
/// </summary>
public class AddonCredential
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>SHA-256 hex hash of the raw token (64 chars).</summary>
    public string TokenHash { get; set; } = null!;

    /// <summary>Optional human label so one device can be told from another.</summary>
    public string? Label { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }

    public bool IsActive => RevokedAt is null;
}

public static class AddonCredentialRevocationReason
{
    public const string Rotated = "Rotated";
    public const string RevokedByUser = "Revoked by user";
}
