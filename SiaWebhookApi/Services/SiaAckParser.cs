using System.Text.RegularExpressions;

namespace SiaWebhookApi.Services;

public static partial class SiaAckParser
{
    public static bool IsAck(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return false;

        var match = ResponseTokenRegex().Match(response);
        return match.Success && string.Equals(match.Groups["token"].Value, "ACK", StringComparison.OrdinalIgnoreCase);
    }

    public static string DescribeFailure(string? response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return "Receiver returned an empty response.";

        var match = ResponseTokenRegex().Match(response);
        if (!match.Success)
            return $"Receiver response did not contain ACK/NAK/DUH: {response}";

        return $"Receiver returned {match.Groups["token"].Value}: {response}";
    }

    [GeneratedRegex("(?<![A-Z])\"?(?<token>ACK|NAK|DUH)\"?(?![A-Z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ResponseTokenRegex();
}
