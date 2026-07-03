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
    [InlineData("   ")]
    [InlineData("NAK")]
    [InlineData("\"NAK\"")]
    [InlineData("DUH")]
    [InlineData("\n12340010\"NAK\"0001\r")]
    [InlineData("not a protocol response")]
    public void IsAck_RejectedOrUnknownResponses_ReturnFalse(string? response)
    {
        Assert.False(SiaAckParser.IsAck(response));
    }

    [Fact]
    public void IsAck_NullResponse_ReturnsFalse()
    {
        Assert.False(SiaAckParser.IsAck(null));
    }

    [Fact]
    public void DescribeFailure_Nak_NamesTheToken()
    {
        Assert.Contains("NAK", SiaAckParser.DescribeFailure("\"NAK\""));
    }

    [Fact]
    public void DescribeFailure_Empty_SaysEmpty()
    {
        Assert.Contains("empty", SiaAckParser.DescribeFailure(""), StringComparison.OrdinalIgnoreCase);
    }
}
