namespace SiaWebhookApi.Models;

public class SiaMessage
{
    // "SIA-DCS" - the protocol identifier, tells the receiver what format this is
    public string EventId { get; set; } = "SIA-DCS";

    // "0001" - increments with each message sent, resets to 0001 after 9999
    public string SequenceNumber { get; set; } = "0001";

    // "R0" - receiver number, R0 means not used
    public string ReceiverNumber { get; set; } = "R0";

    // "L0" - account prefix, L0 means not used
    public string AccountPrefix { get; set; } = "L0";

    // "A001" - identifies which device/account is sending the alarm
    public string AccountNumber { get; set; } = "A001";

    // "NPA" - the SIA event code, NPA = panic alarm
    public string EventCode { get; set; } = "NPA";

    // GPS coordinates coming in from Zepcam
    public string Latitude { get; set; } = "52.3676";
    public string Longitude { get; set; } = "4.9041";

    // Main data from the Zepcam message
    public string ExtendedData { get; set; } = "";

    // When the alarm happened, will be formatted to SIA format: _HH:MM:SS,MM-DD-YYYY
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
