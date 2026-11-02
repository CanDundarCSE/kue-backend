using Kue.Api.Dtos.Auth;
using Kue.Api.Dtos.Common;

namespace Kue.Api.Services;

public interface IAuthService
{
    Task<MessageResponseDto> RegisterAsync(RegisterRequest request, string? ipAddress = null, CancellationToken cancellationToken = default);
    Task<AuthResponseDto> VerifyEmailAsync(VerifyEmailRequest request, string? ipAddress = null, CancellationToken cancellationToken = default);
    Task<MessageResponseDto> ResendVerificationAsync(ResendVerificationRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponseDto> LoginAsync(LoginRequest request, string? ipAddress = null, CancellationToken cancellationToken = default);
    Task<AuthResponseDto> RefreshTokenAsync(string refreshToken, string? ipAddress = null, CancellationToken cancellationToken = default);
    Task RevokeRefreshTokenAsync(string refreshToken, string? ipAddress = null, CancellationToken cancellationToken = default);
    Task ChangePasswordAsync(int userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);
    Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);
    Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);
}
