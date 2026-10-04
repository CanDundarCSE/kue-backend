namespace Kue.Api.Entities;

public class RefreshToken
{
    public int Id { get; set; }

    // SHA-256 hex hash of the raw token; the raw value is never persisted.
    public string Token { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public bool IsRevoked { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    // Which path revoked this token (see RefreshTokenRevocationReason).
    public string? RevocationReason { get; set; }

    // Hash of the token this one was replaced with during rotation, or null.
    public string? ReplacedByToken { get; set; }

    public string? CreatedByIp { get; set; }
    public string? RevokedByIp { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public bool IsActive => !IsRevoked && RevokedAtUtc == null && DateTime.UtcNow < ExpiresAt;
}

public static class RefreshTokenRevocationReason
{
    public const string Rotated = "Rotated";
    public const string ManualLogout = "Manual Logout";
    public const string ReplayAttackDetected = "Replay Attack Detected";
    public const string PasswordChanged = "Password Changed";
    public const string PasswordReset = "Password Reset";
    public const string AccountBanned = "Account Banned";
}
