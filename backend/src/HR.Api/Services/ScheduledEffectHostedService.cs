using HR.Application.Engines.Completion;

namespace HR.Api.Services;

/// <summary>Polls for due deferred completion effects every 60s and drains them. Mirrors
/// EmailDeliveryHostedService: a scope per tick, failures logged and swallowed so the loop survives.</summary>
public sealed class ScheduledEffectHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledEffectHostedService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    public ScheduledEffectHostedService(IServiceScopeFactory scopeFactory, ILogger<ScheduledEffectHostedService> logger)
    { _scopeFactory = scopeFactory; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var drainer = scope.ServiceProvider.GetRequiredService<IScheduledEffectDrainer>();
                var count = await drainer.DrainAsync(stoppingToken);
                if (count > 0) _logger.LogInformation("Scheduled-effect worker processed {Count} effect(s).", count);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Scheduled-effect worker tick failed."); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
