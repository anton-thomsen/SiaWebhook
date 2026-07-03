using System.Net.Sockets;

namespace SiaWebhookApi.Services;

public sealed class SiaTransmitter : ISiaTransmitter
{
    private readonly ILogger<SiaTransmitter> _logger;

    public SiaTransmitter(ILogger<SiaTransmitter> logger)
    {
        _logger = logger;
    }

    public async Task<string> SendAsync(string ipAddress, int port, string message)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            using TcpClient client = new();
            await client.ConnectAsync(ipAddress, port, cts.Token);

            // Empty message means connectivity test only — don't send anything
            if (string.IsNullOrEmpty(message))
                return "OK";

            NetworkStream stream = client.GetStream();
            stream.ReadTimeout  = 5000;
            stream.WriteTimeout = 5000;

            byte[] sendBuffer = System.Text.Encoding.ASCII.GetBytes(message);
            await stream.WriteAsync(sendBuffer, cts.Token);

            byte[] readBuffer = new byte[1024];
            int bytesRead = await stream.ReadAsync(readBuffer, cts.Token);
            string response = System.Text.Encoding.ASCII.GetString(readBuffer, 0, bytesRead);

            _logger.LogDebug("TCP response from {Ip}:{Port} — {Response}", ipAddress, port, response);
            return response;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("TCP connection to {Ip}:{Port} timed out", ipAddress, port);
            return "ERROR: Connection timed out after 5 seconds";
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "SocketException connecting to {Ip}:{Port}", ipAddress, port);
            return "ERROR: " + ex.Message;
        }
    }
}
