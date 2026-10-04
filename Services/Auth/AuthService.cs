using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Kue.Api.Data;
using Kue.Api.Dtos.Auth;
using Kue.Api.Entities;
using Kue.Api.Services.Background;
using Kue.Api.Services.Email;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;

namespace Kue.Api.Services;

public class AuthService : IAuthService
{
    private const int PasswordResetTokenLifetimeHours = 1;
    private const int PasswordResetCooldownSeconds = 60;
    private const int DefaultRotationGracePeriodSeconds = 30;
    private const int MaxRotationChainHops = 10;

    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly IEmailService _emailService;
    private readonly IBackgroundTaskQueue _backgroundTaskQueue;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<AuthService> _logger;
    private readonly int _rotationGracePeriodSeconds;

    public AuthService(
        AppDbContext context,
        IConfiguration configuration,
        IPasswordHasher<User> passwordHasher,
        IEmailService emailService,
        IBackgroundTaskQueue backgroundTaskQueue,
        IMemoryCache memoryCache,
        ILogger<AuthService> logger)
    {
        _context = context;
        _configuration = configuration;
        _passwordHasher = passwordHasher;
        _emailService = emailService;
        _backgroundTaskQueue = backgroundTaskQueue;
        _memoryCache = memoryCache;
        _logger = logger;
        _rotationGracePeriodSeconds = int.TryParse(configuration["Jwt:RefreshTokenRotationGracePeriodSeconds"], out var grace) && grace > 0
            ? grace
            : DefaultRotationGracePeriodSeconds;
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterRequest request, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        // 1. Check if user already exists (generic message to prevent user enumeration)
        if (await _context.Users.AnyAsync(u => u.Email == request.Email || u.Username == request.Username, cancellationToken))
        {
            throw new InvalidOperationException("Registration failed. Please try a different email or username.");
        }

        // 2. Create new user
        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = _passwordHasher.HashPassword(null!, request.Password),
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);

        // 3. Prepare response DTO & save refresh token to DB
        var userDto = MapToUserDto(user);
        var response = await GenerateAuthResponseAsync(userDto);
        await SaveRefreshTokenAsync(user.Id, response.RefreshToken!, ipAddress, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return response;
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequest request, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        // 1. Find user
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email, cancellationToken);

        if (user is null)
        {
            // Run a dummy hash verification to prevent user enumeration via timing attacks
            _passwordHasher.VerifyHashedPassword(null!, DummyPasswordHash, request.Password);
            throw new UnauthorizedAccessException("Invalid credentials.");
        }

        // 2. Verify password
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

        // 3. Clean up expired or revoked tokens
        var obsoleteTokens = await _context.RefreshTokens
            .Where(r => r.UserId == user.Id && (r.ExpiresAt <= DateTime.UtcNow || (r.IsRevoked && r.RevokedAtUtc < DateTime.UtcNow.AddDays(-7))))
            .ToListAsync(cancellationToken);
        if (obsoleteTokens.Count > 0)
        {
            _context.RefreshTokens.RemoveRange(obsoleteTokens);
        }

        // 4. Prepare response DTO & save refresh token to DB
        var userDto = MapToUserDto(user);
        var response = await GenerateAuthResponseAsync(userDto);
        await SaveRefreshTokenAsync(user.Id, response.RefreshToken!, ipAddress, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);

        return response;
    }

    public async Task<AuthResponseDto> RefreshTokenAsync(string refreshToken, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        // 1. Find refresh token in DB (tokens are stored as SHA-256 hashes).
        // AsNoTracking: the atomic claim below mutates the row outside the change
        // tracker, so a tracked copy would go stale when a racing request wins.
        var tokenHash = HashToken(refreshToken);
        var storedToken = await _context.RefreshTokens.AsNoTracking()
            .Include(r => r.User)
            .FirstOrDefaultAsync(r => r.Token == tokenHash, cancellationToken);

        if (storedToken is null)
        {
            throw new UnauthorizedAccessException("Invalid refresh token.");
        }

        if (storedToken.User.IsBanned)
        {
            throw new UnauthorizedAccessException("Your account has been suspended.");
        }

        if (!storedToken.IsRevoked)
        {
            if (storedToken.ExpiresAt <= DateTime.UtcNow)
            {
                throw new UnauthorizedAccessException("Expired refresh token.");
            }

            // 2. Atomically claim this token for rotation. Only one of N racing
            // requests (multi-tab reload, parallel API calls) can claim it; the
            // losers re-read the freshly rotated state and hit the grace path.
            var response = await TryRotateActiveTokenAsync(storedToken, ipAddress, cancellationToken);
            if (response is not null)
            {
                return response;
            }

            var reloadedToken = await _context.RefreshTokens.AsNoTracking()
                .Include(r => r.User)
                .FirstOrDefaultAsync(r => r.Token == tokenHash, cancellationToken);

            if (reloadedToken is null)
            {
                // The row was removed in parallel (expired-token cleanup).
                throw new UnauthorizedAccessException("Invalid refresh token.");
            }

            return await HandleRevokedTokenAsync(reloadedToken, ipAddress, cancellationToken);
        }

        // 3. Revoked token: grace window for a legitimate race, or full revocation
        // for a genuine replay outside the window.
        return await HandleRevokedTokenAsync(storedToken, ipAddress, cancellationToken);
    }

    /// <summary>
    /// Claims <paramref name="storedToken"/> for rotation inside a single transaction
    /// and issues the replacement pair. Returns null if a racing request claimed the
    /// token first.
    /// </summary>
    private async Task<AuthResponseDto?> TryRotateActiveTokenAsync(RefreshToken storedToken, string? ipAddress, CancellationToken cancellationToken)
    {
        var userDto = MapToUserDto(storedToken.User);
        var (accessToken, expiresAt, expiryMinutes) = CreateAccessToken(userDto);
        var newRefreshToken = GenerateRefreshToken();
        var newTokenHash = HashToken(newRefreshToken);
        var now = DateTime.UtcNow;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // Single conditional UPDATE: Postgres serializes concurrent claims on the
        // row lock and re-checks the predicate after the winner commits, so a
        // racing requester updates 0 rows instead of forking the rotation chain.
        var claimed = await _context.RefreshTokens
            .Where(r => r.Token == storedToken.Token && !r.IsRevoked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.IsRevoked, true)
                .SetProperty(r => r.RevokedAtUtc, now)
                .SetProperty(r => r.RevocationReason, RefreshTokenRevocationReason.Rotated)
                .SetProperty(r => r.ReplacedByToken, newTokenHash)
                .SetProperty(r => r.RevokedByIp, ipAddress), cancellationToken);

        if (claimed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        var newEntity = new RefreshToken
        {
            UserId = storedToken.UserId,
            Token = newTokenHash,
            ExpiresAt = now.AddDays(GetRefreshTokenExpirationDays()),
            CreatedAt = now,
            CreatedByIp = ipAddress,
            IsRevoked = false
        };

        _context.RefreshTokens.Add(newEntity);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = newRefreshToken,
            TokenType = "Bearer",
            ExpiresIn = expiryMinutes,
            ExpiresAt = expiresAt,
            User = userDto
        };
    }

    /// <summary>
    /// Handles a replay of an already-revoked token.
    /// Within the rotation grace period a replay of a rotated token is treated as a
    /// legitimate concurrent race (e.g. a second browser tab still holding the old
    /// token): the user is issued a fresh pair and NO sessions are revoked.
    /// Outside the window (or when no live replacement chain exists) this is treated
    /// as token theft and every active session for the user is revoked.
    /// </summary>
    private async Task<AuthResponseDto> HandleRevokedTokenAsync(RefreshToken storedToken, string? ipAddress, CancellationToken cancellationToken)
    {
        var user = storedToken.User;
        var now = DateTime.UtcNow;
        var withinGracePeriod = storedToken.RevokedAtUtc.HasValue
            && now <= storedToken.RevokedAtUtc.Value.AddSeconds(_rotationGracePeriodSeconds);

        if (withinGracePeriod && storedToken.RevocationReason == RefreshTokenRevocationReason.Rotated)
        {
            var activeHead = await FindActiveRotationHeadAsync(storedToken, cancellationToken);
            if (activeHead is not null)
            {
                _logger.LogInformation(
                    "Refresh token replayed within the {GracePeriod}s grace period for user {UserId}; issuing a replacement without revoking sessions.",
                    _rotationGracePeriodSeconds, user.Id);

                return await IssueNewTokenPairAsync(user, ipAddress, cancellationToken);
            }
        }

        if (withinGracePeriod)
        {
            // Revoked for another reason (manual logout, password change, ...) or the
            // replacement chain is already dead. Return 401 WITHOUT cascading: the
            // family is already invalid, and cascading would let one stale token wipe
            // out the user's live sessions.
            throw new UnauthorizedAccessException("This session is no longer active. Please log in again.");
        }

        // Outside the grace window this is a genuine replay / leak (RFC 6749 reuse
        // detection): revoke every active refresh token for this user.
        _logger.LogWarning(
            "Refresh token replay detected outside the grace period for user {UserId}; revoking all active sessions.",
            user.Id);

        await _context.RefreshTokens
            .Where(r => r.UserId == user.Id && !r.IsRevoked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.IsRevoked, true)
                .SetProperty(r => r.RevokedAtUtc, now)
                .SetProperty(r => r.RevocationReason, RefreshTokenRevocationReason.ReplayAttackDetected)
                .SetProperty(r => r.RevokedByIp, ipAddress), cancellationToken);

        throw new UnauthorizedAccessException("Refresh token reuse detected. All active sessions have been revoked.");
    }

    /// <summary>
    /// Walks the rotation chain (via ReplacedByToken) from the revoked token to its
    /// currently active head. Returns null when the chain has no live token.
    /// </summary>
    private async Task<RefreshToken?> FindActiveRotationHeadAsync(RefreshToken startingToken, CancellationToken cancellationToken)
    {
        var current = startingToken;
        var visited = new HashSet<string> { current.Token };

        for (var hop = 0; hop < MaxRotationChainHops; hop++)
        {
            var successorHash = current.ReplacedByToken;
            if (string.IsNullOrEmpty(successorHash))
            {
                return null;
            }

            var successor = await _context.RefreshTokens.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Token == successorHash, cancellationToken);

            if (successor is null || !visited.Add(successor.Token))
            {
                return null;
            }

            if (!successor.IsRevoked && successor.ExpiresAt > DateTime.UtcNow)
            {
                return successor;
            }

            current = successor;
        }

        return null;
    }

    private async Task<AuthResponseDto> IssueNewTokenPairAsync(User user, string? createdByIp, CancellationToken cancellationToken)
    {
        var userDto = MapToUserDto(user);
        var (accessToken, expiresAt, expiryMinutes) = CreateAccessToken(userDto);
        var refreshToken = GenerateRefreshToken();

        await SaveRefreshTokenAsync(user.Id, refreshToken, createdByIp, cancellationToken);

        return new AuthResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            TokenType = "Bearer",
            ExpiresIn = expiryMinutes,
            ExpiresAt = expiresAt,
            User = userDto
        };
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken, string? ipAddress = null, CancellationToken cancellationToken = default)
    {
        var tokenHash = HashToken(refreshToken);
        var storedToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == tokenHash, cancellationToken);

        if (storedToken != null && !storedToken.IsRevoked)
        {
            storedToken.IsRevoked = true;
            storedToken.RevokedAtUtc = DateTime.UtcNow;
            storedToken.RevocationReason = RefreshTokenRevocationReason.ManualLogout;
            storedToken.RevokedByIp = ipAddress;
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
        var now = DateTime.UtcNow;
        await _context.RefreshTokens
            .Where(r => r.UserId == user.Id && !r.IsRevoked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.IsRevoked, true)
                .SetProperty(r => r.RevokedAtUtc, now)
                .SetProperty(r => r.RevocationReason, RefreshTokenRevocationReason.PasswordChanged), cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (user is null)
        {
            // Do not reveal user existence
            return;
        }

        // Rate limiting caps requests per IP, but not repeated mail to a single
        // victim reached from many addresses. Suppress duplicates for a short window.
        // Checked before the token is rotated so a live emailed link is not invalidated.
        var cooldownKey = $"password_reset_cooldown_{user.Id}";
        if (_memoryCache.TryGetValue(cooldownKey, out _))
        {
            _logger.LogInformation("Password reset email for user {UserId} suppressed by cooldown", user.Id);
            return;
        }

        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var resetToken = Convert.ToHexString(tokenBytes);
        // Store only a hash of the reset token so a DB leak cannot be used to reset passwords
        user.PasswordResetToken = HashToken(resetToken);
        user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddHours(PasswordResetTokenLifetimeHours);

        await _context.SaveChangesAsync(cancellationToken);

        // Set before the configuration check below so a misconfigured deployment cannot be
        // used to rotate tokens on every request.
        _memoryCache.Set(cooldownKey, true, TimeSpan.FromSeconds(PasswordResetCooldownSeconds));

        var frontendUrl = _configuration["App:FrontendUrl"];
        if (string.IsNullOrWhiteSpace(frontendUrl))
        {
            // Logged, never thrown. Throwing here would return 500 only when the account
            // exists and 200 otherwise, which is a user enumeration oracle. The token is
            // deliberately discarded: with no link to deliver it must never be returned.
            _logger.LogError("App:FrontendUrl is missing in configuration; password reset email was not sent.");
            return;
        }

        // Convert.ToHexString only emits [0-9A-F], so the token needs no URL encoding.
        var resetLink = $"{frontendUrl.TrimEnd('/')}/reset-password"
                        + $"?token={resetToken}&email={Uri.EscapeDataString(user.Email)}";

        // Handed to the background queue rather than awaited, so a slow or failing mail
        // provider cannot make this branch measurably slower than the account-not-found one.
        await _backgroundTaskQueue.EnqueueAsync(
            (serviceProvider, ct) => serviceProvider.GetRequiredService<IEmailService>()
                .SendPasswordResetEmailAsync(user.Email, user.Username, resetLink, ct),
            cancellationToken);
    }

    public async Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);
        var requestTokenHash = HashToken(request.Token ?? string.Empty);
        if (user is null
            || string.IsNullOrWhiteSpace(user.PasswordResetToken)
            || !FixedTimeEquals(user.PasswordResetToken, requestTokenHash)
            || user.PasswordResetTokenExpiresAt is null
            || user.PasswordResetTokenExpiresAt <= DateTime.UtcNow)
        {
            throw new InvalidOperationException("Invalid or expired password reset token.");
        }

        user.PasswordHash = _passwordHasher.HashPassword(user, request.NewPassword);
        user.PasswordResetToken = null;
        user.PasswordResetTokenExpiresAt = null;

        // Revoke active sessions
        var now = DateTime.UtcNow;
        await _context.RefreshTokens
            .Where(r => r.UserId == user.Id && !r.IsRevoked)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.IsRevoked, true)
                .SetProperty(r => r.RevokedAtUtc, now)
                .SetProperty(r => r.RevocationReason, RefreshTokenRevocationReason.PasswordReset), cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);
    }

    // --- Helper Methods ---

    private Task<AuthResponseDto> GenerateAuthResponseAsync(AuthUserDto user)
    {
        var (accessToken, expiresAt, expiryMinutes) = CreateAccessToken(user);
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

    private (string AccessToken, DateTimeOffset ExpiresAt, int ExpiryMinutes) CreateAccessToken(AuthUserDto user)
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

        return (accessToken, expiresAt, expiryMinutes);
    }

    private int GetRefreshTokenExpirationDays()
    {
        if (!int.TryParse(_configuration["Jwt:RefreshTokenExpirationDays"], out var expiryDays)) expiryDays = 7;
        return expiryDays;
    }

    private async Task SaveRefreshTokenAsync(int userId, string token, string? createdByIp, CancellationToken ct)
    {
        // Store only a SHA-256 hash of the refresh token (see RefreshTokenAsync lookup)
        var tokenHash = HashToken(token);
        var now = DateTime.UtcNow;
        var refreshTokenEntity = new RefreshToken
        {
            UserId = userId,
            Token = tokenHash,
            ExpiresAt = now.AddDays(GetRefreshTokenExpirationDays()),
            IsRevoked = false,
            CreatedAt = now,
            CreatedByIp = createdByIp
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

    /// <summary>
    /// Pre-computed PBKDF2 hash used for dummy verification when a login email does not exist,
    /// so response timing does not reveal whether the account exists.
    /// </summary>
    private static readonly string DummyPasswordHash =
        new PasswordHasher<User>().HashPassword(null!, "DummyPasswordForTimingSafety!");

    private static string HashToken(string token)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hashBytes);
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var aBytes = Encoding.UTF8.GetBytes(a);
        var bBytes = Encoding.UTF8.GetBytes(b);
        return aBytes.Length == bBytes.Length && CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
    }
}