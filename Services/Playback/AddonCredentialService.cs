using Kue.Api.Data;
using Kue.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Services.Playback;

public interface IAddonCredentialService
{
    /// <summary>Creates (or rotates) the single active credential and returns the raw token once.</summary>
    Task<(string RawToken, AddonCredential Credential)> CreateAsync(int userId, CancellationToken ct = default);

    /// <summary>Resolves an active credential by raw token, or null when unknown/revoked.</summary>
    Task<AddonCredential?> ValidateAsync(string rawToken, CancellationToken ct = default);

    Task<AddonCredential?> GetActiveAsync(int userId, CancellationToken ct = default);

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

    public async Task<(string RawToken, AddonCredential Credential)> CreateAsync(int userId, CancellationToken ct = default)
    {
        var now = _time.GetUtcNow().UtcDateTime;

        // One active credential per user keeps rotation unambiguous: generating
        // a new URL invalidates the previous one instead of leaving orphans.
        var active = await _context.AddonCredentials
            .Where(c => c.UserId == userId && c.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var old in active)
        {
            old.RevokedAt = now;
            old.RevokedReason = AddonCredentialRevocationReason.Rotated;
        }

        var rawToken = AddonTokenHasher.GenerateToken();
        var credential = new AddonCredential
        {
            UserId = userId,
            TokenHash = AddonTokenHasher.Hash(rawToken),
            CreatedAt = now
        };

        _context.AddonCredentials.Add(credential);
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation("Created addon credential {CredentialId} for user {UserId}", credential.Id, userId);

        return (rawToken, credential);
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

    public Task<AddonCredential?> GetActiveAsync(int userId, CancellationToken ct = default)
    {
        return _context.AddonCredentials
            .AsNoTracking()
            .Where(c => c.UserId == userId && c.RevokedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
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
