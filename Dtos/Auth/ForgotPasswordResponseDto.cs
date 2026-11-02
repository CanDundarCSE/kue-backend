namespace Kue.Api.Dtos.Auth;

/// <summary>
/// Identical for existing and non-existing accounts. Carries no reset code: the code
/// exists only inside the email, so a caller cannot reset a password it does not own.
/// </summary>
public record ForgotPasswordResponseDto(string Message);
