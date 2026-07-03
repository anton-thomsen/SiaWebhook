namespace SiaWebhookApi.Services;

public interface IAlertNotifier
{
    Task NotifyDeadLetteredAsync(DeadLetteredAlarm alarm, CancellationToken cancellationToken = default);
}
