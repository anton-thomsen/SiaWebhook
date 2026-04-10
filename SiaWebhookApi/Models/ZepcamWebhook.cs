using System.Text.Json;
using System.Text.Json.Serialization;

namespace SiaWebhookApi.Models;

public class Location
{
    [JsonPropertyName("latitude")] public string Latitude { get; set; }

    [JsonPropertyName("longitude")] public string Longitude { get; set; }
}

public class DataTransferObject
{
    // Camera ID
    [JsonPropertyName("user_externalid")]
    public string UserExternalId { get; set; }

    // The person wearing the camera
    [JsonPropertyName("user_name")]
    public string UserName { get; set; }

    // Time stamp incoming format (YYYY-MM-DDTHH:MM:SS.
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; }

    // Don't know what this informatino contains
    [JsonPropertyName("issuer")]
    public string Issuer { get; set; }

    // Location that is gotten from the latitude and longitude. These are nested properties that's why they are in a seperated class.
    [JsonPropertyName("location")]
    public Location Location { get; set; }
}
