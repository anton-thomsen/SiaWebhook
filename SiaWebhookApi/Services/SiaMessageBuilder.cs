using SiaWebhookApi.Models;

namespace SiaWebhookApi.Services;

public class SiaMessageBuilder
{
    private readonly SiaCryptoService? _crypto;

    // If no crypto service is provided, the message will be sent unencrypted
    public SiaMessageBuilder(SiaCryptoService? crypto = null)
    {
        _crypto = crypto;
    }

    public string Build(SiaMessage message)
    {
        bool encrypted = _crypto != null;

        // The * flag tells the receiver this message is encrypted (spec 5.5.1.4.1)
        string idToken = encrypted ? $"\"*{message.EventId}\"" : $"\"{message.EventId}\"";

        // The header is never encrypted
        string header = $"{idToken}{message.SequenceNumber}{message.ReceiverNumber}{message.AccountPrefix}#{message.AccountNumber}";

        // The data region is what goes between [ and \r — this is what gets encrypted
        string dataRegion = $"#{message.AccountNumber}|{message.EventCode}{message.AccountNumber}]" +
                            $"[G{message.Latitude},{message.Longitude}]" +
                            $"[Z{message.ExtendedData}]" +
                            $"_{message.Timestamp.UtcDateTime:HH:mm:ss,MM-dd-yyyy}";

        if (encrypted)
        {
            // Encrypt the data region and wrap in [ ]
            string encryptedHex = _crypto!.EncryptDataRegion(dataRegion);
            return $"{header}[{encryptedHex}";
        }
        else
        {
            // Unencrypted — just wrap normally
            return $"{header}[{dataRegion}";
        }
    }
}
