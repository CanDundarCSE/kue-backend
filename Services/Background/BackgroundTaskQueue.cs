using System.Threading.Channels;

namespace Kue.Api.Services.Background;

/// <summary>
/// Drains queued work on a single background loop, one dependency injection scope
/// per work item.
/// <para>
/// Third-party calls such as transactional email must not be awaited inline on an
/// authentication endpoint. A slow or failing provider would otherwise make the
/// "account exists" branch measurably slower than the "account does not exist"
/// branch, which is a user enumeration oracle.
/// </para>
/// </summary>
public class BackgroundTaskQueue : BackgroundService, IBackgroundTaskQueue
{
    private readonly Channel<Func<IServiceProvider, CancellationToken, Task>> _channel =
        Channel.CreateUnbounded<Func<IServiceProvider, CancellationToken, Task>>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundTaskQueue> _logger;

    public BackgroundTaskQueue(IServiceScopeFactory scopeFactory, ILogger<BackgroundTaskQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public ValueTask EnqueueAsync(Func<IServiceProvider, CancellationToken, Task> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return _channel.Writer.WriteAsync(work, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var work in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    // A dedicated scope keeps the work item independent of the request scope
                    // that scheduled it, which has already been disposed by this point.
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    await work(scope.ServiceProvider, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Work items log their own failures; the drain loop must survive them.
                    _logger.LogError(ex, "Queued background task failed and was discarded");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutdown.
        }
    }
}
