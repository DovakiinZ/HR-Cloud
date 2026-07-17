using HR.Modules.Platform.Services.Notifications;

namespace HR.Api.Services;

/// <summary>Drains the email queue every 60s (emails should feel timely). Mirrors ReportScheduleHostedService.</summary>
public sealed class EmailDeliveryHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailDeliveryHostedService> _logger;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    public EmailDeliveryHostedService(IServiceScopeFactory scopeFactory, ILogger<EmailDeliveryHostedService> logger)
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
                var drainer = scope.ServiceProvider.GetRequiredService<IEmailQueueDrainer>();
                var count = await drainer.DrainAsync(stoppingToken);
                if (count > 0) _logger.LogInformation("Email delivery sent {Count} message(s).", count);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Email delivery tick failed."); }

            try { await Task.Delay(Interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
