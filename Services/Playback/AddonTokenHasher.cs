using System.Security.Cryptography;
using System.Text;

namespace Kue.Api.Services.Playback;

public static class AddonTokenHasher
{
    /// <summary>SHA-256 hex hash; mirrors the existing refresh-token convention.</summary>
    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    /// <summary>32 random bytes as lowercase hex: URL-safe, 256 bits of entropy.</summary>
    public static string GenerateToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    }
}
