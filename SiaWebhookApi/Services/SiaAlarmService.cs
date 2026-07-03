using System.Text;
using SiaWebhookApi.Configuration;
using SiaWebhookApi.Models;

namespace SiaWebhookApi.Services;

public class SiaAlarmService : ISiaAlarmService
{
    private readonly ISiaTransmitter _transmitter;
    private readonly SiaSequenceCounter _seqCounter;
    private readonly ILogger<SiaAlarmService> _logger;

    public SiaAlarmService(ISiaTransmitter transmitter, SiaSequenceCounter seqCounter, ILogger<SiaAlarmService> logger)
    {
        _transmitter = transmitter;
        _seqCounter  = seqCounter;
        _logger      = logger;
    }

    public async Task<string> SendAlarmAsync(DataTransferObject data, SiaSettings settings)
    {
        var siaMessage = new SiaMessage
        {
            AccountNumber  = settings.AccountNumber,
            ReceiverNumber = settings.ReceiverNumber,
            AccountPrefix  = settings.AccountPrefix,
            EventCode      = settings.EventCode,
            SequenceNumber = _seqCounter.Next(),
            Latitude       = data.Location!.Latitude!,
            Longitude      = data.Location.Longitude!,
            ExtendedData   = $"{data.UserName}|{data.UserExternalId}|{data.Issuer}"
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

        string response = await _transmitter.SendAsync(settings.IpAddress, settings.Port, finalMessage);

        if (response.StartsWith("ERROR:"))
            _logger.LogError("SIA alarm send failed: {Response}", response);
        else
            _logger.LogInformation("SIA alarm acknowledged: {Response}", response);

        return response;
    }
}
