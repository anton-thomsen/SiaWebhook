using System.Net.Http.Json;

namespace SiaWebhookApi.Services;

public sealed class N8nAlertNotifier : IAlertNotifier
{
    private readonly HttpClient _httpClient;
    private readonly SiaDeliveryOptions _options;
    private readonly ILogger<N8nAlertNotifier> _logger;

    public N8nAlertNotifier(HttpClient httpClient, SiaDeliveryOptions options, ILogger<N8nAlertNotifier> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task NotifyDeadLetteredAsync(DeadLetteredAlarm alarm, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.AlertWebhookUrl))
        {
            _logger.LogCritical("SIA alarm {DeadLetterId} dead-lettered, but ALERT_WEBHOOK_URL is not configured.", alarm.Id);
            return;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.AlertTimeout);

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(_options.AlertWebhookUrl, alarm, timeout.Token);
            if (!response.IsSuccessStatusCode)
                _logger.LogCritical("Alert webhook returned {StatusCode} for dead-lettered alarm {DeadLetterId}.", response.StatusCode, alarm.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _logger.LogCritical(ex, "Failed to send alert webhook for dead-lettered alarm {DeadLetterId}.", alarm.Id);
        }
    }
}
