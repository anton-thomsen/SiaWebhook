using System.Net.Sockets;

namespace SiaWebhookApi.Services;

public class SiaTransmitter
{
    public static async Task<string> Connect(string ipAddress, int port, string message)
    {
        try
        {
            // The using declaration ensures that the TcpClient is disposed of when the block is exited.
            using TcpClient client = new TcpClient(ipAddress, port);
            // Convert the message to bytes using ASCII encoding.
            Byte[] data = System.Text.Encoding.ASCII.GetBytes(message);
            // This opens a stream between the client and the server.
            NetworkStream stream = client.GetStream();
            // This writes the data to the stream.
            await stream.WriteAsync(data);
            // This recieves the data from the stream
            int bytes = await stream.ReadAsync(data);
            // This converts the data back to a string using ASCII encoding.
            string responseData = System.Text.Encoding.ASCII.GetString(data, 0, bytes);
            Console.WriteLine(responseData);
            return responseData;
        }
        catch (ArgumentNullException ex)
        {
            Console.WriteLine("ArgumentNullException: " + ex.Message);
            return "ERROR: " + ex.Message;
        }
        catch (SocketException ex)
        {
            Console.WriteLine("SocketException: " + ex.Message);
            return "ERROR: " + ex.Message;
        }
    }
}
