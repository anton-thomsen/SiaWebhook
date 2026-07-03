using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SiaWebhookApi.Configuration;
using SiaWebhookApi.Models;
using SiaWebhookApi.Services;

namespace SiaWebhookApi.Tests;

/// <summary>
/// Integration tests for the 8 KB request body limit on /zepcam-panic.
/// A legitimate Zepcam payload is well under 1 KB; anything larger than 8 KB
/// must be rejected with 413 before it is buffered or processed.
/// </summary>
public class ZepcamPanicBodySizeLimitTests : IDisposable
{
    private const string ApiKey = "body-limit-test-key";

    private readonly WebApplicationFactory<Program> _factory;

    public ZepcamPanicBodySizeLimitTests()
    {
        Environment.SetEnvironmentVariable("ZEPCAM_API_KEY", ApiKey);

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // Stub the alarm service so tests never open a real TCP connection.
            builder.ConfigureServices(services =>
            {
                var alarmService = new Mock<ISiaAlarmService>();
                alarmService
                    .Setup(s => s.SendAlarmAsync(It.IsAny<DataTransferObject>(), It.IsAny<SiaSettings>()))
                    .ReturnsAsync("ACK");
                services.AddSingleton(alarmService.Object);
            });
        });

        // The in-memory TestServer does not enforce request body size limits —
        // run the real Kestrel server so the 8 KB cap is actually exercised.
        _factory.UseKestrel();
    }

    public void Dispose()
    {
        _factory.Dispose();
        Environment.SetEnvironmentVariable("ZEPCAM_API_KEY", null);
    }

    private HttpRequestMessage BuildRequest(string userName)
    {
        var json = $$"""
            {
                "user_name": "{{userName}}",
                "user_externalid": "guard-001",
                "issuer": "zepcam",
                "location": { "latitude": "57.7089", "longitude": "11.9746" }
            }
            """;

        var request = new HttpRequestMessage(HttpMethod.Post, "/zepcam-panic")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        return request;
    }

    [Fact]
    public async Task NormalSizedBody_IsProcessed()
    {
        using var client = _factory.CreateClient();

        var response = await client.SendAsync(BuildRequest("Guard One"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task BodyOver8Kb_IsRejectedWith413()
    {
        using var client = _factory.CreateClient();

        // Padding pushes the total body well past the 8192-byte limit.
        var response = await client.SendAsync(BuildRequest(new string('x', 9000)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }
}
