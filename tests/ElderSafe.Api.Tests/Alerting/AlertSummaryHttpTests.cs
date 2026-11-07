using System.Net;
using System.Net.Http.Json;
using ElderSafe.Api.Alerting;
using ElderSafe.Api.Simulation;
using ElderSafe.Api.Tests.Security;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ElderSafe.Api.Tests.Alerting;

public class AlertSummaryHttpTests
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

    private static string RangeQuery(DateTimeOffset from, DateTimeOffset to)
    {
        return $"from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
    }

    [Fact(DisplayName = "REQ-FALL-09 a simulated fall is counted in the summary")]
    [Trait("Requirement", "REQ-FALL-09")]
    public async Task A_simulated_fall_is_counted_in_the_summary()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory);

        var simulated = await client.PostAsync(
            $"/api/residents/{ResidentA}/simulate/{ScenarioLibrary.Fall}", null);
        Assert.Equal(HttpStatusCode.OK, simulated.StatusCode);

        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow.AddDays(1);

        var response = await client.GetAsync(
            $"/api/residents/{ResidentA}/alerts/summary?{RangeQuery(from, to)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<AlertSummary>();

        Assert.NotNull(summary);
        var total = summary!.Buckets
            .SelectMany(b => b.Counts)
            .Where(c => c.Kind == "Fall")
            .Sum(c => c.Total);

        Assert.Equal(1, total);
    }

    [Fact(DisplayName = "REQ-SEC-01 a caregiver cannot see the summary of an unassigned resident")]
    [Trait("Requirement", "REQ-SEC-01")]
    public async Task A_caregiver_cannot_see_the_summary_of_an_unassigned_resident()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory);

        var from = DateTimeOffset.UtcNow.AddDays(-7);
        var to = DateTimeOffset.UtcNow;

        var response = await client.GetAsync(
            $"/api/residents/{ResidentB}/alerts/summary?{RangeQuery(from, to)}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-03 an unauthenticated caller cannot read a summary")]
    [Trait("Requirement", "REQ-SEC-03")]
    public async Task An_unauthenticated_caller_cannot_read_a_summary()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = factory.CreateClient();

        var from = DateTimeOffset.UtcNow.AddDays(-7);
        var to = DateTimeOffset.UtcNow;

        var response = await client.GetAsync(
            $"/api/residents/{ResidentA}/alerts/summary?{RangeQuery(from, to)}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-FALL-09 a range where 'from' is not before 'to' is rejected")]
    [Trait("Requirement", "REQ-FALL-09")]
    public async Task A_range_where_from_is_not_before_to_is_rejected()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory);

        var now = DateTimeOffset.UtcNow;

        var response = await client.GetAsync(
            $"/api/residents/{ResidentA}/alerts/summary?{RangeQuery(now, now.AddDays(-1))}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-FALL-09 a range wider than the maximum is rejected")]
    [Trait("Requirement", "REQ-FALL-09")]
    public async Task A_range_wider_than_the_maximum_is_rejected()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory);

        var to = DateTimeOffset.UtcNow;
        var from = to - AlertSummaryService.MaxRange - TimeSpan.FromDays(1);

        var response = await client.GetAsync(
            $"/api/residents/{ResidentA}/alerts/summary?{RangeQuery(from, to)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-FALL-09 a resident with no alerts in range gets all-zero buckets")]
    [Trait("Requirement", "REQ-FALL-09")]
    public async Task A_resident_with_no_alerts_in_range_gets_all_zero_buckets()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory);

        var from = DateTimeOffset.UtcNow.AddDays(-7);
        var to = DateTimeOffset.UtcNow;

        var response = await client.GetAsync(
            $"/api/residents/{ResidentA}/alerts/summary?{RangeQuery(from, to)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<AlertSummary>();

        Assert.NotNull(summary);
        Assert.NotEmpty(summary!.Buckets);
        Assert.All(
            summary.Buckets.SelectMany(b => b.Counts),
            count => Assert.Equal(0, count.Total));
        Assert.Empty(summary.Alerts);
    }

    [Fact(DisplayName = "REQ-FALL-10 a simulated fall appears in the summary's alert list, unacknowledged")]
    [Trait("Requirement", "REQ-FALL-10")]
    public async Task A_simulated_fall_appears_in_the_summary_alert_list_unacknowledged()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory);

        var simulated = await client.PostAsync(
            $"/api/residents/{ResidentA}/simulate/{ScenarioLibrary.Fall}", null);
        Assert.Equal(HttpStatusCode.OK, simulated.StatusCode);

        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow.AddDays(1);

        var response = await client.GetAsync(
            $"/api/residents/{ResidentA}/alerts/summary?{RangeQuery(from, to)}");

        var summary = await response.Content.ReadFromJsonAsync<AlertSummary>();

        Assert.NotNull(summary);
        var entry = Assert.Single(summary!.Alerts);
        Assert.Equal("Fall", entry.Kind);
        Assert.Null(entry.AcknowledgedAt);
        Assert.Null(entry.AcknowledgedByDisplayName);
        Assert.False(entry.EmergencyCalled);
    }

    [Fact(DisplayName = "REQ-FALL-10 once acknowledged, the summary's alert entry names who acknowledged it")]
    [Trait("Requirement", "REQ-FALL-10")]
    public async Task Once_acknowledged_the_summary_alert_entry_names_who_acknowledged_it()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory);

        var simulated = await client.PostAsync(
            $"/api/residents/{ResidentA}/simulate/{ScenarioLibrary.Fall}", null);
        Assert.Equal(HttpStatusCode.OK, simulated.StatusCode);

        var from = DateTimeOffset.UtcNow.AddDays(-1);
        var to = DateTimeOffset.UtcNow.AddDays(1);

        var beforeAck = await client.GetAsync(
            $"/api/residents/{ResidentA}/alerts/summary?{RangeQuery(from, to)}");
        var beforeSummary = await beforeAck.Content.ReadFromJsonAsync<AlertSummary>();
        var alertId = Assert.Single(beforeSummary!.Alerts).Id;

        var acknowledged = await client.PostAsync($"/api/alerts/{alertId}/acknowledge", null);
        Assert.Equal(HttpStatusCode.NoContent, acknowledged.StatusCode);

        var afterAck = await client.GetAsync(
            $"/api/residents/{ResidentA}/alerts/summary?{RangeQuery(from, to)}");
        var afterSummary = await afterAck.Content.ReadFromJsonAsync<AlertSummary>();

        var entry = Assert.Single(afterSummary!.Alerts);
        Assert.NotNull(entry.AcknowledgedAt);
        Assert.False(string.IsNullOrWhiteSpace(entry.AcknowledgedByDisplayName));
    }
}
