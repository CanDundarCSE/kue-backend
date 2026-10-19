namespace Kue.Api.Dtos.Users;

/// <summary>
/// Short-lived token the browser uses to authenticate its SignalR connection.
/// Returned only to an already-authenticated caller, so the long-lived access
/// token can stay HttpOnly.
/// </summary>
public record HubTokenResponseDto
{
    public string Token { get; init; } = null!;
    public DateTimeOffset ExpiresAt { get; init; }
}
