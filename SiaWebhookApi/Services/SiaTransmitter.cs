using System.Net.Sockets;
using System.Text;

namespace SiaWebhookApi.Services;

public sealed class SiaTransmitter : ISiaTransmitter
{
    private readonly ILogger<SiaTransmitter> _logger;

    public SiaTransmitter(ILogger<SiaTransmitter> logger)
    {
        _logger = logger;
    }

    public async Task<SiaTransmissionResult> SendAsync(
        string ipAddress,
        int port,
        string message,
        CancellationToken cancellationToken = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using TcpClient client = new();
            await client.ConnectAsync(ipAddress, port, cts.Token);

            // Empty message means connectivity test only — don't send anything
            if (string.IsNullOrEmpty(message))
                return SiaTransmissionResult.Ack("OK");

            NetworkStream stream = client.GetStream();
            stream.ReadTimeout  = 5000;
            stream.WriteTimeout = 5000;

            byte[] sendBuffer = Encoding.ASCII.GetBytes(message);
            await stream.WriteAsync(sendBuffer, cts.Token);

            byte[] readBuffer = new byte[1024];
            int bytesRead = await stream.ReadAsync(readBuffer, cts.Token);
            string response = Encoding.ASCII.GetString(readBuffer, 0, bytesRead);

            _logger.LogDebug("TCP response from {Ip}:{Port} — {Response}", ipAddress, port, response);
            return SiaAckParser.IsAck(response)
                ? SiaTransmissionResult.Ack(response)
                : SiaTransmissionResult.Failed(SiaAckParser.DescribeFailure(response), response);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("TCP connection to {Ip}:{Port} timed out", ipAddress, port);
            return SiaTransmissionResult.Failed("Connection timed out after 5 seconds");
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "SocketException connecting to {Ip}:{Port}", ipAddress, port);
            return SiaTransmissionResult.Failed(ex.Message);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "I/O error communicating with {Ip}:{Port}", ipAddress, port);
            return SiaTransmissionResult.Failed(ex.Message);
        }
    }
}
