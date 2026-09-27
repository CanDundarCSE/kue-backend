namespace Kue.Api.Services.Background;

/// <summary>
/// Accepts work to run outside the request pipeline.
/// <para>
/// The work item receives a fresh <see cref="IServiceProvider"/> from a dedicated
/// dependency injection scope, so it can safely resolve scoped services (such as
/// the Brevo email service) after the originating HTTP request has already ended.
/// </para>
/// </summary>
public interface IBackgroundTaskQueue
{
    ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken ct = default);
}
