using SiaWebhookApi.Configuration;
using SiaWebhookApi.Models;

namespace SiaWebhookApi.Services;

public interface ISiaAlarmService
{
    Task<SiaAlarmDeliveryResult> SendAlarmAsync(DataTransferObject data, SiaSettings settings, CancellationToken cancellationToken = default);
}
