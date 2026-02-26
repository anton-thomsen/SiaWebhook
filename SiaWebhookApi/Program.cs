var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// This is the Webhook catcher
app.MapPost("/zepcam-panic", async (ZepcamData data) =>
{
    // 1. We got the HTTP JSON from Zepcam
    Console.WriteLine($"[ALERT] Received from Camera: {data.CameraId}");

    // 2. Build the payload and do the math (calling your separate SiaMath class!)
    string targetPayload = $"\"SIA-DCS\"0001R0L0#{data.CameraId}[#{data.CameraId}|NPA001]_12:09:30,02-23-2026";
    string lengthHex = SiaMath.CalculateLength(targetPayload);
    string crcHex = SiaMath.CalculateCrc(targetPayload);
    string finalMessage = $"\n{crcHex}{lengthHex}{targetPayload}\r";

    // 3. Send it via TCP (We will use the SiaTransmitter class for this soon)
    // string response = await SiaTransmitter.SendAsync("123.45.67.89", 10001, finalMessage);

    return Results.Ok(new { Status = "Processed" });
});

app.Run();

public record ZepcamData(string CameraId, string AlertType, DateTime Timestamp);