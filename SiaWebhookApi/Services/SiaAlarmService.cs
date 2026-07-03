using System.Text;
using SiaWebhookApi.Configuration;
using SiaWebhookApi.Models;

namespace SiaWebhookApi.Services;

public class SiaAlarmService : ISiaAlarmService
{
    private readonly ISiaTransmitter _transmitter;
    private readonly SiaSequenceCounter _seqCounter;
    private readonly IDeadLetterStore _deadLetterStore;
    private readonly IAlertNotifier _alertNotifier;
    private readonly SiaDeliveryOptions _options;
    private readonly ILogger<SiaAlarmService> _logger;

    public SiaAlarmService(
        ISiaTransmitter transmitter,
        SiaSequenceCounter seqCounter,
        IDeadLetterStore deadLetterStore,
        IAlertNotifier alertNotifier,
        SiaDeliveryOptions options,
        ILogger<SiaAlarmService> logger)
    {
        _transmitter     = transmitter;
        _seqCounter      = seqCounter;
        _deadLetterStore = deadLetterStore;
        _alertNotifier   = alertNotifier;
        _options         = options;
        _logger          = logger;
    }

    public async Task<SiaAlarmDeliveryResult> SendAlarmAsync(
        DataTransferObject data,
        SiaSettings settings,
        CancellationToken cancellationToken = default)
    {
        var sequenceNumber = _seqCounter.Next();
        var siaMessage = new SiaMessage
        {
            AccountNumber  = settings.AccountNumber,
            ReceiverNumber = settings.ReceiverNumber,
            AccountPrefix  = settings.AccountPrefix,
            EventCode      = settings.EventCode,
            SequenceNumber = sequenceNumber,
            Latitude       = data.Location!.Latitude!,
            Longitude      = data.Location.Longitude!,
            ExtendedData   = $"{data.UserName}|{data.UserExternalId}|{data.Issuer}",
            Timestamp      = data.Timestamp
        };

        SiaMessageBuilder messageBuilder = string.IsNullOrEmpty(settings.EncryptionKey)
            ? new SiaMessageBuilder()
            : new SiaMessageBuilder(new SiaCryptoService(Encoding.ASCII.GetBytes(settings.EncryptionKey)));

        string payload      = messageBuilder.Build(siaMessage);
        ZepcamWebhookValidator.EnsurePayloadFits(payload);
        string lengthHex    = SiaMath.CalculateLength(payload);
        string crcHex       = SiaMath.CalculateCrc(payload);
        string finalMessage = $"\n{crcHex}{lengthHex}{payload}\r";

        _logger.LogInformation("Sending SIA alarm seq={Seq} account={Account} event={Event} to {Ip}:{Port}",
            siaMessage.SequenceNumber, siaMessage.AccountNumber, siaMessage.EventCode,
            settings.IpAddress, settings.Port);

        var errors = new List<string>();
        for (var attempt = 1; attempt <= _options.MaxAttempts; attempt++)
        {
            var response = await _transmitter.SendAsync(settings.IpAddress, settings.Port, finalMessage, cancellationToken);
            if (response.Success)
            {
                _logger.LogInformation("SIA alarm acknowledged on attempt {Attempt}: {Response}", attempt, response.Response);
                return SiaAlarmDeliveryResult.Delivered(response.Response, errors);
            }

            var error = $"{DateTimeOffset.UtcNow:O} attempt {attempt}: {response.Error ?? response.Response ?? "unknown failure"}";
            errors.Add(error);
            _logger.LogError("SIA alarm send attempt {Attempt}/{MaxAttempts} failed: {Error}", attempt, _options.MaxAttempts, error);

            if (attempt < _options.MaxAttempts)
            {
                var delay = TimeSpan.FromMilliseconds(_options.InitialRetryDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));
                await Task.Delay(delay, cancellationToken);
            }
        }

        var deadLetter = new DeadLetteredAlarm
        {
            Id = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}",
            Webhook = data,
            SiaMessage = finalMessage,
            SequenceNumber = sequenceNumber,
            AccountNumber = settings.AccountNumber,
            ReceiverEndpoint = $"{settings.IpAddress}:{settings.Port}",
            Encrypted = !string.IsNullOrEmpty(settings.EncryptionKey),
            Attempts = _options.MaxAttempts,
            Errors = errors
        };

        var id = await _deadLetterStore.SaveAsync(deadLetter, cancellationToken);
        await _alertNotifier.NotifyDeadLetteredAsync(deadLetter, cancellationToken);
        return SiaAlarmDeliveryResult.Failed("SIA receiver did not acknowledge the alarm.", id, errors);
    }
}
