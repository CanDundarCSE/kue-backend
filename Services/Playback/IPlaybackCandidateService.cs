using Kue.Api.Dtos.Library;
using Kue.Api.Dtos.Playback;
using Kue.Api.Entities;

namespace Kue.Api.Services.Playback;

public enum PlaybackConfirmOutcome
{
    Confirmed,
    NotFound,
    Unresolved
}

public sealed record PlaybackConfirmResult(PlaybackConfirmOutcome Outcome, LibraryEntryDto? Entry);

public interface IPlaybackCandidateService
{
    /// <summary>
    /// Records one addon subtitle request as a playback-start candidate.
    /// Returns null when the request is not a valid movie/series id.
    /// </summary>
    Task<PlaybackCandidate?> RecordAsync(int userId, string? type, string? rawId, CancellationToken ct = default);

    Task<List<PlaybackCandidateDto>> ListPendingAsync(int userId, CancellationToken ct = default);

    /// <summary>
    /// Explicit user confirmation. This is the only path from a candidate to
    /// watch history; playback itself never writes a library entry.
    /// </summary>
    Task<PlaybackConfirmResult> ConfirmAsync(int userId, int candidateId, CancellationToken ct = default);

    Task<bool> DismissAsync(int userId, int candidateId, CancellationToken ct = default);
}
