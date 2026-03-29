using SiaWebhookApi.Models;
using SiaWebhookApi.Services;
using SiaWebhookApi.Configuration;

var builder = WebApplication.CreateBuilder(args);
var siaSettings = builder.Configuration.GetSection("SiaSettings").Get<SiaSettings>();
if (siaSettings == null)
    throw new InvalidOperationException("SiaSettings missing from appsettings.json");
var app = builder.Build();

// This is the Webhook catcher
app.MapPost("/zepcam-panic", async (DataTransferObject data) =>
{
    // 1. Create the SIA message object with hardcoded defaults for now
    var siaMessage = new SiaMessage()
    {
        Latitude = data.Location.Latitude,
        Longitude = data.Location.Longitude,
        ExtendedData = $"{data.UserName}|{data.UserExternalId}|{data.Issuer}"
    };

    // 2. Get the encryption key and build the message builder
    SiaMessageBuilder messageBuilder;
    if (siaSettings.EncryptionKey == null)
        messageBuilder = new SiaMessageBuilder();
    else
        messageBuilder = new SiaMessageBuilder(new SiaCryptoService(System.Text.Encoding.ASCII.GetBytes(siaSettings.EncryptionKey)));

    // 3. Build the payload string from the SIA message properties
    string payload = messageBuilder.Build(siaMessage);

    // 4. Calculate length and CRC on the payload
    string lengthHex = SiaMath.CalculateLength(payload);
    string crcHex = SiaMath.CalculateCrc(payload);

    // 5. Wrap the final message with the frame delimiters
    string finalMessage = $"\n{crcHex}{lengthHex}{payload}\r";

    Console.WriteLine($"[SIA] Final message: {finalMessage}");

    // 6. Send via TCP (coming soon)
    string response = await SiaTransmitter.Connect(siaSettings.IpAddress, siaSettings.Port, finalMessage);

    return Results.Ok(new { Status = "Processed" });
});

app.Run();
