using Kue.Api.Data;
using Kue.Api.Services.Background;
using Kue.Api.Services.Import;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Services.Import;

/// <summary>
/// On startup, jobs left in a non-terminal state by a crash or deploy are
/// reset to pending and re-queued so they resume instead of stalling forever.
/// </summary>
public class ImportRecoveryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportRecoveryService> _logger;

    public ImportRecoveryService(IServiceScopeFactory scopeFactory, ILogger<ImportRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var queue = scope.ServiceProvider.GetRequiredService<IBackgroundTaskQueue>();

            var interrupted = await context.ImportJobs
                .Where(j => j.Status == "running" || j.Status == "applying")
                .ToListAsync(stoppingToken);

            foreach (var job in interrupted)
            {
                var jobId = job.Id;
                if (job.Status == "applying")
                {
                    // Decisions were already recorded; resume applying.
                    await queue.EnqueueAsync((sp, token) =>
                        sp.GetRequiredService<ImportProcessor>().ApplyAsync(jobId, token), stoppingToken);
                }
                else
                {
                    job.Status = "pending";
                    job.UpdatedAt = DateTime.UtcNow;
                    await queue.EnqueueAsync((sp, token) =>
                        sp.GetRequiredService<ImportProcessor>().ProcessAsync(jobId, token), stoppingToken);
                }
                _logger.LogInformation("Re-queued interrupted import job {JobId}.", jobId);
            }

            if (interrupted.Count > 0)
            {
                await context.SaveChangesAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down; nothing to recover.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import recovery sweep failed.");
        }
    }
}
