using SiaWebhookApi.Services;

namespace SiaWebhookApi.Tests;

public class SiaAckParserTests
{
    [Theory]
    [InlineData("ACK")]
    [InlineData("\"ACK\"")]
    [InlineData("\n12340010\"ACK\"0001\r")]
    public void IsAck_AckResponses_ReturnTrue(string response)
    {
        Assert.True(SiaAckParser.IsAck(response));
    }

    [Theory]
    [InlineData("")]
    [InlineData("NAK")]
    [InlineData("DUH")]
    [InlineData("not a protocol response")]
    public void IsAck_RejectedOrUnknownResponses_ReturnFalse(string response)
    {
        Assert.False(SiaAckParser.IsAck(response));
    }
}
