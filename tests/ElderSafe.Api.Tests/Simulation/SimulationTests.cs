using System.Net;
using System.Net.Http.Json;
using ElderSafe.Api.Controllers;
using ElderSafe.Api.Simulation;
using ElderSafe.Api.Tests.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ElderSafe.Api.Tests.Simulation;

public class SimulationTests
{
    private const string FirstCaregiver = "demo.caregiver@example.invalid";

    private static readonly Guid ResidentA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ResidentB = new("22222222-2222-2222-2222-222222222222");

    private static async Task<HttpClient> SignedInAsync(ElderSafeWebAppFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        var response = await client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { UserName = FirstCaregiver, Password = ElderSafeWebAppFactory.DemoPassword });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        return client;
    }

    [Fact(DisplayName = "REQ-FALL-01 the fall scenario raises exactly one alert")]
    [Trait("Requirement", "REQ-FALL-01")]
    public async Task The_fall_scenario_raises_exactly_one_alert()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory);

        var response = await client.PostAsync(
            $"/api/residents/{ResidentA}/simulate/{ScenarioLibrary.Fall}", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SimulationResponse>();

        Assert.NotNull(result);
        Assert.NotNull(result!.AlertId);

        var alerts = await client.GetFromJsonAsync<List<AlertListItem>>(
            $"/api/residents/{ResidentA}/alerts");

        Assert.NotNull(alerts);
        Assert.Single(alerts!);
    }

    [Fact(DisplayName = "REQ-FALL-01 the ordinary movement scenario raises no alert")]
    [Trait("Requirement", "REQ-FALL-01")]
    public async Task The_ordinary_movement_scenario_raises_no_alert()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory);

        var response = await client.PostAsync(
            $"/api/residents/{ResidentA}/simulate/{ScenarioLibrary.OrdinaryMovement}", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SimulationResponse>();

        Assert.NotNull(result);
        Assert.Null(result!.AlertId);
    }

    [Fact(DisplayName = "REQ-SEC-01 a caregiver cannot simulate for an unassigned resident")]
    [Trait("Requirement", "REQ-SEC-01")]
    public async Task A_caregiver_cannot_simulate_for_an_unassigned_resident()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory);

        var response = await client.PostAsync(
            $"/api/residents/{ResidentB}/simulate/{ScenarioLibrary.Fall}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-03 an unauthenticated caller cannot trigger a simulation")]
    [Trait("Requirement", "REQ-SEC-03")]
    public async Task An_unauthenticated_caller_cannot_trigger_a_simulation()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync(
            $"/api/residents/{ResidentA}/simulate/{ScenarioLibrary.Fall}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-FALL-01 an unknown scenario is rejected")]
    [Trait("Requirement", "REQ-FALL-01")]
    public async Task An_unknown_scenario_is_rejected()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory);

        var response = await client.PostAsync(
            $"/api/residents/{ResidentA}/simulate/not-a-scenario", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-04 the simulate endpoint does not answer when simulation is disabled")]
    [Trait("Requirement", "REQ-SEC-04")]
    public async Task The_simulate_endpoint_does_not_answer_when_simulation_is_disabled()
    {
        using var factory = new SimulationDisabledFactory();
        var client = await SignedInAsync(factory);

        var response = await client.PostAsync(
            $"/api/residents/{ResidentA}/simulate/{ScenarioLibrary.Fall}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed record AlertListItem(Guid Id, Guid ResidentId, string Kind);

    private sealed class SimulationDisabledFactory : ElderSafeWebAppFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.UseSetting("Simulation:Enabled", "false");
        }
    }
}
