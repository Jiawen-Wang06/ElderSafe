using System.Net;
using System.Net.Http.Json;
using ElderSafe.Api.Data;
using ElderSafe.Api.Data.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ElderSafe.Api.Tests.Security;

public class AuthenticationTests : IClassFixture<ElderSafeWebAppFactory>
{
    private const string FirstCaregiver = "demo.caregiver@example.invalid";
    private const string SecondCaregiver = "demo.nurse@example.invalid";

    private static readonly Guid ResidentA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ResidentB = new("22222222-2222-2222-2222-222222222222");

    private readonly ElderSafeWebAppFactory _factory;

    public AuthenticationTests(ElderSafeWebAppFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
    }

    private async Task<HttpClient> SignedInAsAsync(string userName)
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { UserName = userName, Password = ElderSafeWebAppFactory.DemoPassword });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        return client;
    }

    [Fact(DisplayName = "REQ-SEC-03 refuses an unauthenticated request for resident alerts")]
    [Trait("Requirement", "REQ-SEC-03")]
    public async Task Refuses_an_unauthenticated_request_for_resident_alerts()
    {
        var client = CreateClient();

        var response = await client.GetAsync($"/api/residents/{ResidentA}/alerts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-03 refuses an unauthenticated acknowledgement")]
    [Trait("Requirement", "REQ-SEC-03")]
    public async Task Refuses_an_unauthenticated_acknowledgement()
    {
        var client = CreateClient();

        var response = await client.PostAsync($"/api/alerts/{Guid.NewGuid()}/acknowledge", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-03 signs in with the seeded demo credentials")]
    [Trait("Requirement", "REQ-SEC-03")]
    public async Task Signs_in_with_the_seeded_demo_credentials()
    {
        var client = await SignedInAsAsync(FirstCaregiver);

        var me = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-03 refuses a wrong password")]
    [Trait("Requirement", "REQ-SEC-03")]
    public async Task Refuses_a_wrong_password()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { UserName = FirstCaregiver, Password = "not-the-password" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-03 refuses an unknown user with the same response as a wrong password")]
    [Trait("Requirement", "REQ-SEC-03")]
    public async Task Refuses_an_unknown_user_with_the_same_response_as_a_wrong_password()
    {
        var client = CreateClient();

        var unknown = await client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { UserName = "nobody@example.invalid", Password = "not-the-password" });

        var wrongPassword = await client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { UserName = FirstCaregiver, Password = "also-not-the-password" });

        Assert.Equal(wrongPassword.StatusCode, unknown.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-01 returns alerts for a resident the caregiver is assigned to")]
    [Trait("Requirement", "REQ-SEC-01")]
    public async Task Returns_alerts_for_a_resident_the_caregiver_is_assigned_to()
    {
        var client = await SignedInAsAsync(FirstCaregiver);

        var response = await client.GetAsync($"/api/residents/{ResidentA}/alerts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-01 returns 404 for a resident the caregiver is not assigned to")]
    [Trait("Requirement", "REQ-SEC-01")]
    public async Task Returns_404_for_a_resident_the_caregiver_is_not_assigned_to()
    {
        var client = await SignedInAsAsync(FirstCaregiver);

        var response = await client.GetAsync($"/api/residents/{ResidentB}/alerts");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-01 returns the same response for an unassigned resident and one that does not exist")]
    [Trait("Requirement", "REQ-SEC-01")]
    public async Task Returns_the_same_response_for_an_unassigned_resident_and_one_that_does_not_exist()
    {
        var client = await SignedInAsAsync(FirstCaregiver);

        var unassigned = await client.GetAsync($"/api/residents/{ResidentB}/alerts");
        var missing = await client.GetAsync($"/api/residents/{Guid.NewGuid()}/alerts");

        Assert.Equal(missing.StatusCode, unassigned.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-02 records the signed-in caregiver as the actor on acknowledgement")]
    [Trait("Requirement", "REQ-SEC-02")]
    public async Task Records_the_signed_in_caregiver_as_the_actor_on_acknowledgement()
    {
        var alertId = await AddOpenAlertAsync(ResidentA);

        var client = await SignedInAsAsync(FirstCaregiver);

        var response = await client.PostAsync($"/api/alerts/{alertId}/acknowledge", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ElderSafeDbContext>();

        var alert = await context.Alerts.AsNoTracking().SingleAsync(a => a.Id == alertId);
        var caregiver = await context.Users.AsNoTracking().SingleAsync(u => u.UserName == FirstCaregiver);

        Assert.Equal(caregiver.Id, alert.AcknowledgedByCaregiverId);

        Assert.Contains(
            context.AuditEntries.AsNoTracking().ToList(),
            e => e.Action == "AcknowledgedAlert" && e.ActorCaregiverId == caregiver.Id);
    }

    [Fact(DisplayName = "REQ-SEC-01 refuses to acknowledge an alert for an unassigned resident")]
    [Trait("Requirement", "REQ-SEC-01")]
    public async Task Refuses_to_acknowledge_an_alert_for_an_unassigned_resident()
    {
        var alertId = await AddOpenAlertAsync(ResidentB);

        var client = await SignedInAsAsync(FirstCaregiver);

        var response = await client.PostAsync($"/api/alerts/{alertId}/acknowledge", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<Guid> AddOpenAlertAsync(Guid residentId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ElderSafeDbContext>();

        var now = DateTimeOffset.UtcNow;

        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            ResidentId = residentId,
            Kind = ElderSafe.Api.Alerting.AlertKind.Fall,
            RaisedAt = now,
            LastNotifiedAt = now,
            EscalationLevel = 0
        };

        context.Alerts.Add(alert);

        await context.SaveChangesAsync();

        return alert.Id;
    }
}
