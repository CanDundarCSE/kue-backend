namespace Kue.Api.Dtos.Auth;

public record ForgotPasswordResponseDto(string Message, string? ResetToken = null);
