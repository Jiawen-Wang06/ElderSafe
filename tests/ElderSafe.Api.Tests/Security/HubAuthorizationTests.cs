using System.Net;
using System.Net.Http.Json;

namespace ElderSafe.Api.Tests.Security;

public class HubAuthorizationTests : IClassFixture<ElderSafeWebAppFactory>
{
    private const string HubNegotiate = "/hubs/alerts/negotiate?negotiateVersion=1";
    private const string FirstCaregiver = "demo.caregiver@example.invalid";

    private readonly ElderSafeWebAppFactory _factory;

    public HubAuthorizationTests(ElderSafeWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact(DisplayName = "REQ-SEC-03 refuses an unauthenticated connection to the alerts hub")]
    [Trait("Requirement", "REQ-SEC-03")]
    public async Task Refuses_an_unauthenticated_connection_to_the_alerts_hub()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync(HubNegotiate, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-04 refuses a device key on the alerts hub")]
    [Trait("Requirement", "REQ-SEC-04")]
    public async Task Refuses_a_device_key_on_the_alerts_hub()
    {
        var client = _factory.CreateClient();

        client.DefaultRequestHeaders.Add(
            ElderSafe.Api.Security.DeviceAuthenticationHandler.HeaderName,
            ElderSafeWebAppFactory.DemoDeviceKey);

        var response = await client.PostAsync(HubNegotiate, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-03 allows a signed-in caregiver to connect to the alerts hub")]
    [Trait("Requirement", "REQ-SEC-03")]
    public async Task Allows_a_signed_in_caregiver_to_connect_to_the_alerts_hub()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        var signIn = await client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { UserName = FirstCaregiver, Password = ElderSafeWebAppFactory.DemoPassword });

        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);

        var response = await client.PostAsync(HubNegotiate, null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
