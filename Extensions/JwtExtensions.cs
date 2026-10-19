using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Kue.Api.Extensions;

public static class JwtExtensions
{
    /// <summary>
    /// Authentication scheme used only by the SignalR notification hub. Hub
    /// tokens are short-lived and carry the hub audience, so a hub token cannot
    /// call the REST API and a normal access token cannot open a hub connection.
    /// </summary>
    public const string HubScheme = "HubBearer";

    public static IServiceCollection AddJwtAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var jwtKey = configuration["Jwt:Key"];

        if (string.IsNullOrEmpty(jwtKey))
        {
            throw new InvalidOperationException("JWT Key is missing in configuration!");
        }

        // HS256 needs a key of at least 256 bits (32 bytes). A shorter key
        // silently degrades HMAC strength for every issued token, so fail fast
        // instead of starting with weak signing. Measured in bytes, not chars:
        // multibyte characters contribute more than one byte.
        if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
        {
            throw new InvalidOperationException("JWT Key must be at least 32 bytes (256 bits) for HS256 signing!");
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var issuer = configuration["Jwt:Issuer"];
        var apiAudience = configuration["Jwt:Audience"];
        var hubAudience = configuration["Jwt:HubAudience"] ?? "KueHub";

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => Configure(options, key, issuer, apiAudience))
            .AddJwtBearer(HubScheme, options => Configure(options, key, issuer, hubAudience));

        services.AddAuthorization();

        return services;
    }

    private static void Configure(
        JwtBearerOptions options,
        SymmetricSecurityKey key,
        string? issuer,
        string? audience)
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = key,
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                // SignalR WebSockets send JWT via query string '?access_token=...'
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                else if (context.Request.Cookies.TryGetValue("accessToken", out var token) && !string.IsNullOrWhiteSpace(token))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    }
}
