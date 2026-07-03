using SiaWebhookApi.Configuration;
using SiaWebhookApi.Models;

namespace SiaWebhookApi.Services;

public interface ISiaAlarmService
{
    Task<string> SendAlarmAsync(DataTransferObject data, SiaSettings settings);
}
