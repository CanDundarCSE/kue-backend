using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Kue.Api.Data;
using Kue.Api.Dtos.Auth;
using Kue.Api.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Kue.Api.Services;

public class AuthService : IAuthService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly PasswordHasher<User> _passwordHasher;

    public AuthService(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
        _passwordHasher = new PasswordHasher<User>();
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Kullanıcı var mı kontrol et
        if (await _context.Users.AnyAsync(u => u.Email == request.Email || u.Username == request.Username, cancellationToken))
        {
            throw new InvalidOperationException("User with this email or username already exists.");
        }

        // 2. Yeni kullanıcı oluştur
        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = _passwordHasher.HashPassword(null!, request.Password),
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        // 3. Response DTO hazırla & Refresh token DB'ye kaydet
        var userDto = MapToUserDto(user);
        var response = await GenerateAuthResponseAsync(userDto);
        await SaveRefreshTokenAsync(user.Id, response.RefreshToken!, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return response;
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Kullanıcıyı bul
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken);
        
        if (user is null)
        {
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        // 2. Şifreyi doğrula
        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        
        if (result != PasswordVerificationResult.Success)
        {
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        // 3. Response DTO hazırla & Refresh token DB'ye kaydet
        var userDto = MapToUserDto(user);
        var response = await GenerateAuthResponseAsync(userDto);
        await SaveRefreshTokenAsync(user.Id, response.RefreshToken!, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return response;
    }

    public async Task<AuthResponseDto> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        // 1. Refresh token'ı DB'de bul
        var storedToken = await _context.RefreshTokens
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == refreshToken && !r.IsRevoked && r.ExpiresAt > DateTime.UtcNow, cancellationToken);

        if (storedToken is null)
        {
            throw new UnauthorizedAccessException("Invalid or expired refresh token.");
        }

        // 2. Eski token'ı geçersiz kıl (One-time use security)
        storedToken.IsRevoked = true;
        
        // 3. Yeni tokenlar üret
        var userDto = MapToUserDto(storedToken.User);
        var response = await GenerateAuthResponseAsync(userDto);

        // 4. Yeni refresh token'ı DB'ye kaydet
        await SaveRefreshTokenAsync(storedToken.UserId, response.RefreshToken!, cancellationToken);
        
        await _context.SaveChangesAsync(cancellationToken);

        return response;
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var storedToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == refreshToken, cancellationToken);

        if (storedToken != null)
        {
            storedToken.IsRevoked = true;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    // --- Helper Methods ---

    private Task<AuthResponseDto> GenerateAuthResponseAsync(AuthUserDto user)
    {
        var jwtKey = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key missing!");
        var issuer = _configuration["Jwt:Issuer"];
        var audience = _configuration["Jwt:Audience"];
        
        if (!int.TryParse(_configuration["Jwt:AccessTokenExpirationMinutes"], out var expiryMinutes)) expiryMinutes = 15;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Email, user.Email)
        };
        foreach (var role in user.Roles) claims.Add(new Claim(ClaimTypes.Role, role));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes);

        var tokenDescriptor = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: creds);
        var accessToken = new JwtSecurityTokenHandler().WriteToken(tokenDescriptor);
        var refreshToken = GenerateRefreshToken();

        return Task.FromResult(new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            TokenType = "Bearer",
            ExpiresIn = expiryMinutes * 60,
            ExpiresAt = expiresAt,
            User = user
        });
    }

    private async Task SaveRefreshTokenAsync(int userId, string token, CancellationToken ct)
    {
        var refreshTokenEntity = new RefreshToken
        {
            UserId = userId,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            IsRevoked = false,
            CreatedAt = DateTime.UtcNow
        };
        await _context.RefreshTokens.AddAsync(refreshTokenEntity, ct);
    }

    private static AuthUserDto MapToUserDto(User user)
    {
        return new AuthUserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            AvatarUrl = user.AvatarUrl,
            Roles = user.Roles ?? new List<string> { "User" }
        };
    }

    private static string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }
}