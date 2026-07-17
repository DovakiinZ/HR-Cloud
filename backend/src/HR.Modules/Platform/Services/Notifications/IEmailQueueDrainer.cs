namespace HR.Modules.Platform.Services.Notifications;

public interface IEmailQueueDrainer
{
    /// <summary>Sends up to a batch of due queued emails. Returns the number successfully sent.</summary>
    Task<int> DrainAsync(CancellationToken ct);
}
