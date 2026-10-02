namespace Kue.Api.Dtos.Auth;

/// <summary>
/// Identical for existing and non-existing accounts. Carries no reset token: the token
/// exists only inside the emailed link, so a caller cannot reset a password it does not own.
/// </summary>
public record ForgotPasswordResponseDto(string Message);
