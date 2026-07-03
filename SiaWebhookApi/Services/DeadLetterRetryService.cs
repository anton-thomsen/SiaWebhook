namespace SiaWebhookApi.Services;

public sealed class DeadLetterRetryService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly SiaDeliveryOptions _options;
    private readonly ILogger<DeadLetterRetryService> _logger;

    public DeadLetterRetryService(IServiceProvider services, SiaDeliveryOptions options, ILogger<DeadLetterRetryService> logger)
    {
        _services = services;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_options.BackgroundRetryInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = _services.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IDeadLetterStore>();
            var transmitter = scope.ServiceProvider.GetRequiredService<ISiaTransmitter>();

            foreach (var alarm in await store.ListAsync(stoppingToken))
            {
                var endpoint = alarm.ReceiverEndpoint.Split(':', 2);
                if (endpoint.Length != 2 || !int.TryParse(endpoint[1], out var port))
                {
                    _logger.LogError("Dead-lettered alarm {DeadLetterId} has invalid receiver endpoint {Endpoint}.", alarm.Id, alarm.ReceiverEndpoint);
                    continue;
                }

                var result = await transmitter.SendAsync(endpoint[0], port, alarm.SiaMessage, stoppingToken);
                if (result.Success)
                {
                    await store.DeleteAsync(alarm.Id, stoppingToken);
                    _logger.LogInformation("Background retry delivered dead-lettered alarm {DeadLetterId}.", alarm.Id);
                    continue;
                }

                var updated = alarm with
                {
                    Attempts = alarm.Attempts + 1,
                    LastAttemptUtc = DateTimeOffset.UtcNow,
                    Errors = alarm.Errors.Concat([$"{DateTimeOffset.UtcNow:O} background retry: {result.Error ?? result.Response ?? "unknown failure"}"]).ToArray()
                };
                await store.UpdateAsync(updated, stoppingToken);
            }
        }
    }
}
