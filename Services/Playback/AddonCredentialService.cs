using Kue.Api.Data;
using Kue.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Services.Playback;

public interface IAddonCredentialService
{
    /// <summary>Active credentials a user may hold at once.</summary>
    const int MaxActiveCredentials = 10;

    /// <summary>
    /// Creates an additional credential and returns the raw token once. Existing
    /// credentials stay valid, so a second device does not disconnect the first.
    /// Returns null when the per-user cap has been reached.
    /// </summary>
    Task<(string RawToken, AddonCredential Credential)?> CreateAsync(
        int userId,
        string? label = null,
        CancellationToken ct = default);

    /// <summary>Resolves an active credential by raw token, or null when unknown/revoked.</summary>
    Task<AddonCredential?> ValidateAsync(string rawToken, CancellationToken ct = default);

    /// <summary>Every credential that has not been revoked, newest first.</summary>
    Task<IReadOnlyList<AddonCredential>> ListActiveAsync(int userId, CancellationToken ct = default);

    /// <summary>Revokes one credential owned by the user. False when it does not exist.</summary>
    Task<bool> RevokeAsync(int userId, int credentialId, CancellationToken ct = default);

    /// <summary>Revokes every active credential for the user. Returns true when one existed.</summary>
    Task<bool> RevokeAllAsync(int userId, CancellationToken ct = default);

    /// <summary>Throttled LastUsedAt update; never blocks the addon response.</summary>
    Task TouchAsync(int credentialId, DateTime now, CancellationToken ct = default);
}

public class AddonCredentialService : IAddonCredentialService
{
    private static readonly TimeSpan TouchInterval = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _context;
    private readonly TimeProvider _time;
    private readonly ILogger<AddonCredentialService> _logger;

    public AddonCredentialService(
        AppDbContext context,
        TimeProvider time,
        ILogger<AddonCredentialService> logger)
    {
        _context = context;
        _time = time;
        _logger = logger;
    }

    public async Task<(string RawToken, AddonCredential Credential)?> CreateAsync(
        int userId,
        string? label = null,
        CancellationToken ct = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        // Multiple credentials are allowed on purpose: each device gets its own
        // URL, and adding one must never disconnect the others. The cap keeps a
        // single account from minting unbounded tokens.
        var activeCount = await _context.AddonCredentials
            .CountAsync(c => c.UserId == userId && c.RevokedAt == null, ct);

        if (activeCount >= IAddonCredentialService.MaxActiveCredentials)
        {
            return null;
        }

        var rawToken = AddonTokenHasher.GenerateToken();
        var credential = new AddonCredential
        {
            UserId = userId,
            TokenHash = AddonTokenHasher.Hash(rawToken),
            Label = NormalizeLabel(label),
            CreatedAt = now
        };

        _context.AddonCredentials.Add(credential);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Created addon credential {CredentialId} for user {UserId}", credential.Id, userId);

        return (rawToken, credential);
    }

    private static string? NormalizeLabel(string? label)
    {
        var trimmed = label?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        return trimmed.Length <= 40 ? trimmed : trimmed[..40];
    }

    public async Task<AddonCredential?> ValidateAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken) || rawToken.Length != 64)
        {
            return null;
        }

        var hash = AddonTokenHasher.Hash(rawToken);
        return await _context.AddonCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TokenHash == hash && c.RevokedAt == null, ct);
    }

    public async Task<IReadOnlyList<AddonCredential>> ListActiveAsync(int userId, CancellationToken ct = default)
    {
        return await _context.AddonCredentials
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.RevokedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .ToListAsync(ct);
    }

    public async Task<bool> RevokeAsync(int userId, int credentialId, CancellationToken ct = default)
    {
        var credential = await _context.AddonCredentials
            .FirstOrDefaultAsync(
                c => c.Id == credentialId && c.UserId == userId && c.RevokedAt == null,
                ct);

        if (credential is null) return false;

        credential.RevokedAt = _time.GetUtcNow().UtcDateTime;
        credential.RevokedReason = AddonCredentialRevocationReason.RevokedByUser;
        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RevokeAllAsync(int userId, CancellationToken ct = default)
    {
        var active = await _context.AddonCredentials
            .Where(c => c.UserId == userId && c.RevokedAt == null)
            .ToListAsync(ct);

        if (active.Count == 0) return false;

        var now = _time.GetUtcNow().UtcDateTime;
        foreach (var credential in active)
        {
            credential.RevokedAt = now;
            credential.RevokedReason = AddonCredentialRevocationReason.RevokedByUser;
        }

        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task TouchAsync(int credentialId, DateTime now, CancellationToken ct = default)
    {
        try
        {
            var credential = await _context.AddonCredentials
                .FirstOrDefaultAsync(c => c.Id == credentialId, ct);

            if (credential is null) return;
            if (credential.LastUsedAt is { } last && now - last < TouchInterval) return;

            credential.LastUsedAt = now;
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Diagnostics must never break the subtitle response.
            _logger.LogDebug(ex, "Could not update addon credential usage time");
        }
    }
}
