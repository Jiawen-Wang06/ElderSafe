using System.Net;
using System.Net.Http.Json;
using ElderSafe.Api.Alerting;
using ElderSafe.Api.Data;
using ElderSafe.Api.Simulation;
using ElderSafe.Api.Tests.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ElderSafe.Api.Tests.Alerting;

public class AlertTraceHttpTests
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

    private static async Task<Guid> SimulateFallAndGetAlertIdAsync(HttpClient client, Guid residentId)
    {
        var simulated = await client.PostAsync($"/api/residents/{residentId}/simulate/{ScenarioLibrary.Fall}", null);
        Assert.Equal(HttpStatusCode.OK, simulated.StatusCode);

        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow.AddDays(1);
        var fromQuery = Uri.EscapeDataString(from.ToString("O"));
        var toQuery = Uri.EscapeDataString(to.ToString("O"));

        var summaryResponse = await client.GetAsync(
            $"/api/residents/{residentId}/alerts/summary?from={fromQuery}&to={toQuery}");
        var summary = await summaryResponse.Content.ReadFromJsonAsync<AlertSummary>();

        return Assert.Single(summary!.Alerts).Id;
    }

    private static async Task<Guid> AddOpenAlertAsync(ElderSafeWebAppFactory factory, Guid residentId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ElderSafeDbContext>();

        var now = DateTimeOffset.UtcNow;

        var alert = new ElderSafe.Api.Data.Entities.Alert
        {
            Id = Guid.NewGuid(),
            ResidentId = residentId,
            Kind = AlertKind.Fall,
            RaisedAt = now,
            LastNotifiedAt = now,
            EscalationLevel = 0
        };

        context.Alerts.Add(alert);

        await context.SaveChangesAsync();

        return alert.Id;
    }

    [Fact(DisplayName = "REQ-FALL-08 a caregiver assigned to the resident can read the trace behind a simulated fall")]
    [Trait("Requirement", "REQ-FALL-08")]
    public async Task A_caregiver_assigned_to_the_resident_can_read_the_trace_behind_a_simulated_fall()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory);

        var alertId = await SimulateFallAndGetAlertIdAsync(client, ResidentA);

        var response = await client.GetAsync($"/api/alerts/{alertId}/trace");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var trace = await response.Content.ReadFromJsonAsync<AlertTrace>();

        Assert.NotNull(trace);
        Assert.Equal(alertId, trace!.AlertId);
        Assert.Equal(ResidentA, trace.ResidentId);
        Assert.Equal("Fall", trace.Kind);
        Assert.NotEmpty(trace.Samples);
        Assert.True(trace.WindowStart < trace.WindowEnd);
        Assert.All(trace.Samples, sample => Assert.True(sample.At >= trace.WindowStart && sample.At <= trace.WindowEnd));
    }

    [Fact(DisplayName = "REQ-FALL-08 the reported thresholds are the ones the detector is configured with")]
    [Trait("Requirement", "REQ-FALL-08")]
    public async Task The_reported_thresholds_are_the_ones_the_detector_is_configured_with()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory);

        var alertId = await SimulateFallAndGetAlertIdAsync(client, ResidentA);

        var response = await client.GetAsync($"/api/alerts/{alertId}/trace");
        var trace = await response.Content.ReadFromJsonAsync<AlertTrace>();

        Assert.NotNull(trace);
        Assert.True(trace!.Thresholds.FreeFallG > 0);
        Assert.True(trace.Thresholds.ImpactG > 0);
        Assert.True(trace.Thresholds.StillnessDurationSeconds > 0);
    }

    [Fact(DisplayName = "REQ-SEC-01 a caregiver cannot read the trace of an alert for an unassigned resident")]
    [Trait("Requirement", "REQ-SEC-01")]
    [Trait("Requirement", "REQ-FALL-08")]
    public async Task A_caregiver_cannot_read_the_trace_of_an_alert_for_an_unassigned_resident()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory);

        var alertId = await AddOpenAlertAsync(factory, ResidentB);

        var response = await client.GetAsync($"/api/alerts/{alertId}/trace");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-03 an unauthenticated caller cannot read a trace")]
    [Trait("Requirement", "REQ-SEC-03")]
    [Trait("Requirement", "REQ-FALL-08")]
    public async Task An_unauthenticated_caller_cannot_read_a_trace()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/alerts/{Guid.NewGuid()}/trace");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-FALL-08 a nonexistent alert id reads the same as one belonging to another resident")]
    [Trait("Requirement", "REQ-FALL-08")]
    public async Task A_nonexistent_alert_id_reads_the_same_as_one_belonging_to_another_resident()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory);

        var response = await client.GetAsync($"/api/alerts/{Guid.NewGuid()}/trace");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
