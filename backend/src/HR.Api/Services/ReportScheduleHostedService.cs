using HR.Modules.Platform.Services.Reports;

namespace HR.Api.Services;

/// <summary>Runs due report schedules shortly after startup, then hourly. RunDueAsync is idempotent
/// per tick (only schedules whose NextRunAt has passed are picked up), so cadence only affects timeliness.</summary>
public sealed class ReportScheduleHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReportScheduleHostedService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public ReportScheduleHostedService(IServiceScopeFactory scopeFactory, ILogger<ReportScheduleHostedService> logger)
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
                var runner = scope.ServiceProvider.GetRequiredService<IReportScheduleRunner>();
                var count = await runner.RunDueAsync(stoppingToken);
                if (count > 0) _logger.LogInformation("Report scheduler processed {Count} schedule(s).", count);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Report scheduler tick failed."); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
