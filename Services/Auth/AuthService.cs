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
    private readonly IPasswordHasher<User> _passwordHasher;

    public AuthService(AppDbContext context, IConfiguration configuration, IPasswordHasher<User> passwordHasher)
    {
        _context = context;
        _configuration = configuration;
        _passwordHasher = passwordHasher;
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

        if (user.IsBanned)
        {
            var reason = string.IsNullOrWhiteSpace(user.BanReason) ? "Your account has been suspended." : $"Your account has been suspended: {user.BanReason}";
            throw new UnauthorizedAccessException(reason);
        }

        // 3. Eski süresi geçmiş veya iptal edilmiş tokenları temizle
        var obsoleteTokens = await _context.RefreshTokens
            .Where(r => r.UserId == user.Id && (r.ExpiresAt <= DateTime.UtcNow || (r.IsRevoked && r.RevokedAt < DateTime.UtcNow.AddDays(-7))))
            .ToListAsync(cancellationToken);
        if (obsoleteTokens.Count > 0)
        {
            _context.RefreshTokens.RemoveRange(obsoleteTokens);
        }

        // 4. Response DTO hazırla & Refresh token DB'ye kaydet
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
            .FirstOrDefaultAsync(r => r.Token == refreshToken, cancellationToken);

        if (storedToken is null)
        {
            throw new UnauthorizedAccessException("Invalid refresh token.");
        }

        if (storedToken.User.IsBanned)
        {
            throw new UnauthorizedAccessException("Your account has been suspended.");
        }

        // 2. Token daha önce revoke edilmişse (Reuse Attack Detection - RFC 6749)
        if (storedToken.IsRevoked)
        {
            // Olası bir token sızıntısı: Kullanıcının tüm aktif refresh token'larını iptal et!
            var compromisedTokens = await _context.RefreshTokens
                .Where(r => r.UserId == storedToken.UserId && !r.IsRevoked)
                .ToListAsync(cancellationToken);

            foreach (var token in compromisedTokens)
            {
                token.IsRevoked = true;
                token.RevokedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);
            throw new UnauthorizedAccessException("Refresh token reuse detected. All active sessions have been revoked.");
        }

        // 3. Süresi geçmiş mi kontrol et
        if (storedToken.ExpiresAt <= DateTime.UtcNow)
        {
            throw new UnauthorizedAccessException("Expired refresh token.");
        }

        // 4. Eski token'ı geçersiz kıl (One-time use / Token Rotation)
        storedToken.IsRevoked = true;
        storedToken.RevokedAt = DateTime.UtcNow;
        
        // 5. Yeni tokenlar üret
        var userDto = MapToUserDto(storedToken.User);
        var response = await GenerateAuthResponseAsync(userDto);

        // 6. Yeni refresh token'ı DB'ye kaydet
        await SaveRefreshTokenAsync(storedToken.UserId, response.RefreshToken!, cancellationToken);
        
        await _context.SaveChangesAsync(cancellationToken);

        return response;
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var storedToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == refreshToken, cancellationToken);

        if (storedToken != null && !storedToken.IsRevoked)
        {
            storedToken.IsRevoked = true;
            storedToken.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            throw new KeyNotFoundException("User not found.");
        }

        var verificationResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword);
        if (verificationResult != PasswordVerificationResult.Success)
        {
            throw new InvalidOperationException("Current password is incorrect.");
        }

        if (request.CurrentPassword == request.NewPassword)
        {
            throw new InvalidOperationException("New password must be different from current password.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);

        // Revoke all existing refresh tokens for security
        var activeTokens = await _context.RefreshTokens
            .Where(r => r.UserId == user.Id && !r.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (user is null)
        {
            // Do not reveal user existence
            return null;
        }

        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var resetToken = Convert.ToHexString(tokenBytes);

        user.PasswordResetToken = resetToken;
        user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddHours(1);

        await _context.SaveChangesAsync(cancellationToken);

        return resetToken;
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (user is null 
            || string.IsNullOrWhiteSpace(user.PasswordResetToken) 
            || user.PasswordResetToken != request.Token 
            || user.PasswordResetTokenExpiresAt is null 
            || user.PasswordResetTokenExpiresAt <= DateTime.UtcNow)
        {
            throw new InvalidOperationException("Invalid or expired password reset token.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiresAt = null;

        // Revoke active sessions
        var activeTokens = await _context.RefreshTokens
            .Where(r => r.UserId == user.Id && !r.IsRevoked)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.IsRevoked = true;
            token.RevokedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
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
        if (!int.TryParse(_configuration["Jwt:RefreshTokenExpirationDays"], out var expiryDays)) expiryDays = 7;

        var refreshTokenEntity = new RefreshToken
        {
            UserId = userId,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddDays(expiryDays),
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
            Bio = user.Bio,
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