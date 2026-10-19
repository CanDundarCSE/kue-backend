using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Kue.Api.Entities;
using Microsoft.IdentityModel.Tokens;

namespace Kue.Api.Services.Auth;

/// <summary>
/// Issues the short-lived token used only by the SignalR notification hub.
/// Kept separate from the normal access token so the two audiences can be
/// validated independently (see JwtExtensions.HubScheme).
/// </summary>
public static class HubTokenFactory
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    public static (string Token, DateTimeOffset ExpiresAt) Create(
        User user,
        IConfiguration configuration)
    {
        var jwtKey = configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("JWT Key missing!");
        var issuer = configuration["Jwt:Issuer"];
        var audience = configuration["Jwt:HubAudience"] ?? "KueHub";

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username)
        };
        foreach (var role in user.Roles ?? [])
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var expiresAt = DateTimeOffset.UtcNow.Add(Lifetime);
        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}
