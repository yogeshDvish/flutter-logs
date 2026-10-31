using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Arovyx.SealedTrace.Infrastructure.Services;

namespace Arovyx.SealedTrace.Jobs;

/// <summary>
/// Retention worker: runs nightly at 03:00 UTC, deletes logs per project policy
/// in 10k-row batches, cleans orphaned batches, and purges expired refresh tokens.
/// </summary>
public sealed partial class RetentionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RetentionBackgroundService> _logger;

    public RetentionBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<RetentionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Retention background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRun = GetNextRunTime();
            var delay = nextRun - DateTimeOffset.UtcNow;

            if (delay > TimeSpan.Zero)
            {
                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var retentionService = scope.ServiceProvider.GetRequiredService<RetentionService>();
                await retentionService.RunRetentionAsync(stoppingToken);
                _logger.LogInformation("Retention run completed.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retention run failed.");
            }
        }

        _logger.LogInformation("Retention background service stopped.");
    }

    private static DateTimeOffset GetNextRunTime()
    {
        var now = DateTimeOffset.UtcNow;
        var todayUtc = DateTime.SpecifyKind(now.DateTime.Date, DateTimeKind.Utc);
        var today3am = new DateTimeOffset(todayUtc, TimeSpan.Zero).AddHours(3);

        if (now > today3am)
            today3am = today3am.AddDays(1);

        return today3am;
    }
}
