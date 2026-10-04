using Kue.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Kue.Api.Services.Background;

public class TokenCleanupBackgroundService : BackgroundService
{
    private const int DefaultIntervalHours = 24;
    private const int MinIntervalHours = 1;
    private const int RevokedTokenRetentionDays = 1;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TokenCleanupBackgroundService> _logger;
    private readonly TimeSpan _interval;

    public TokenCleanupBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<TokenCleanupBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _interval = TimeSpan.FromHours(ParseIntervalHours(configuration["TokenCleanup:IntervalHours"]));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Refresh token cleanup service started (interval: {Interval}, revoked-token retention: {RetentionDays}d).",
            _interval,
            RevokedTokenRetentionDays);

        using var timer = new PeriodicTimer(_interval);
        try
        {
            do
            {
                await RunCleanupAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down; nothing to clean up.
        }
        finally
        {
            _logger.LogInformation("Refresh token cleanup service stopped.");
        }
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var now = DateTime.UtcNow;
            var revokedCutoff = now.AddDays(-RevokedTokenRetentionDays);

            var deletedCount = await context.RefreshTokens
                .Where(t => t.ExpiresAt <= now
                            || (t.IsRevoked && t.RevokedAtUtc <= revokedCutoff))
                .ExecuteDeleteAsync(cancellationToken);

            _logger.LogInformation(
                "Refresh token cleanup completed; deleted {DeletedCount} obsolete token(s).",
                deletedCount);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Refresh token cleanup run cancelled during shutdown.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Refresh token cleanup run failed; the next scheduled run will retry.");
        }
    }

    private static int ParseIntervalHours(string? rawValue)
    {
        return int.TryParse(rawValue, out var hours) && hours >= MinIntervalHours
            ? hours
            : DefaultIntervalHours;
    }
}
