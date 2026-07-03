namespace SiaWebhookApi.Services;

public interface IDeadLetterStore
{
    Task<string> SaveAsync(DeadLetteredAlarm alarm, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DeadLetteredAlarm>> ListAsync(CancellationToken cancellationToken = default);
    Task UpdateAsync(DeadLetteredAlarm alarm, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
