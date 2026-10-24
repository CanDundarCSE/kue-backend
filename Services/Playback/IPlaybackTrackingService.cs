namespace Kue.Api.Services.Playback;

public interface IPlaybackTrackingService
{
    /// <summary>
    /// Applies one validated addon subtitle request to the user's library.
    /// A request means "playback was initialised": it can add a title and
    /// advance episode progress, but it is never treated as completion.
    /// Malformed ids and unresolved media are ignored.
    /// </summary>
    Task ApplyAsync(int userId, string? type, string? rawId, CancellationToken ct = default);
}
