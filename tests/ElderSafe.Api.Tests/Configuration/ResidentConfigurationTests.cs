using System.Net;
using System.Net.Http.Json;
using ElderSafe.Api.Configuration;
using ElderSafe.Api.Data.Entities;
using ElderSafe.Api.Tests.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ElderSafe.Api.Tests.Configuration;

public class ResidentConfigurationTests
{
    private const string ResidentAccount = "demo.resident@example.invalid";
    private const string FirstCaregiver = "demo.caregiver@example.invalid";
    private const string SecondCaregiver = "demo.nurse@example.invalid";

    private static readonly Guid ResidentA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid FirstCaregiverId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SecondCaregiverId = new("44444444-4444-4444-4444-444444444444");

    private static async Task<HttpClient> SignedInAsync(ElderSafeWebAppFactory factory, string userName)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        var response = await client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { UserName = userName, Password = ElderSafeWebAppFactory.DemoPassword });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        return client;
    }

    [Fact(DisplayName = "REQ-CFG-01 a resident account can change its escalation limit and emergency call setting")]
    [Trait("Requirement", "REQ-CFG-01")]
    public async Task A_resident_account_can_change_its_escalation_limit_and_emergency_call_setting()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var update = await client.PutAsJsonAsync(
            $"/api/residents/{ResidentA}/settings",
            new { EscalationLimit = 1, EmergencyCallEnabled = true, EmergencyContactPhoneNumber = (string?)null });

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        var settings = await client.GetFromJsonAsync<ResidentSettingsView>(
            $"/api/residents/{ResidentA}/settings");

        Assert.NotNull(settings);
        Assert.Equal(1, settings!.EscalationLimit);
        Assert.True(settings.EmergencyCallEnabled);
    }

    [Fact(DisplayName = "REQ-CFG-01 an escalation limit below one is refused")]
    [Trait("Requirement", "REQ-CFG-01")]
    public async Task An_escalation_limit_below_one_is_refused()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var response = await client.PutAsJsonAsync(
            $"/api/residents/{ResidentA}/settings",
            new { EscalationLimit = 0, EmergencyCallEnabled = false, EmergencyContactPhoneNumber = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-CFG-01 an escalation limit of exactly ten is accepted")]
    [Trait("Requirement", "REQ-CFG-01")]
    public async Task An_escalation_limit_of_exactly_ten_is_accepted()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var response = await client.PutAsJsonAsync(
            $"/api/residents/{ResidentA}/settings",
            new { EscalationLimit = 10, EmergencyCallEnabled = false, EmergencyContactPhoneNumber = (string?)null });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-CFG-01 an escalation limit above ten is refused")]
    [Trait("Requirement", "REQ-CFG-01")]
    public async Task An_escalation_limit_above_ten_is_refused()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var response = await client.PutAsJsonAsync(
            $"/api/residents/{ResidentA}/settings",
            new { EscalationLimit = 11, EmergencyCallEnabled = false, EmergencyContactPhoneNumber = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-CFG-06 a resident account can set and clear an emergency contact number")]
    [Trait("Requirement", "REQ-CFG-06")]
    public async Task A_resident_account_can_set_and_clear_an_emergency_contact_number()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var set = await client.PutAsJsonAsync(
            $"/api/residents/{ResidentA}/settings",
            new { EscalationLimit = (int?)null, EmergencyCallEnabled = true, EmergencyContactPhoneNumber = "555-0100" });

        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);

        var afterSet = await client.GetFromJsonAsync<ResidentSettingsView>($"/api/residents/{ResidentA}/settings");

        Assert.NotNull(afterSet);
        Assert.Equal("555-0100", afterSet!.EmergencyContactPhoneNumber);

        var clear = await client.PutAsJsonAsync(
            $"/api/residents/{ResidentA}/settings",
            new { EscalationLimit = (int?)null, EmergencyCallEnabled = true, EmergencyContactPhoneNumber = (string?)null });

        Assert.Equal(HttpStatusCode.NoContent, clear.StatusCode);

        var afterClear = await client.GetFromJsonAsync<ResidentSettingsView>($"/api/residents/{ResidentA}/settings");

        Assert.NotNull(afterClear);
        Assert.Null(afterClear!.EmergencyContactPhoneNumber);
    }

    [Fact(DisplayName = "REQ-CFG-06 blank input clears the emergency contact number rather than storing blank text")]
    [Trait("Requirement", "REQ-CFG-06")]
    public async Task Blank_input_clears_the_emergency_contact_number_rather_than_storing_blank_text()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var response = await client.PutAsJsonAsync(
            $"/api/residents/{ResidentA}/settings",
            new { EscalationLimit = (int?)null, EmergencyCallEnabled = false, EmergencyContactPhoneNumber = "   " });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var settings = await client.GetFromJsonAsync<ResidentSettingsView>($"/api/residents/{ResidentA}/settings");

        Assert.NotNull(settings);
        Assert.Null(settings!.EmergencyContactPhoneNumber);
    }

    [Fact(DisplayName = "REQ-CFG-06 an emergency contact number longer than the cap is refused")]
    [Trait("Requirement", "REQ-CFG-06")]
    public async Task An_emergency_contact_number_longer_than_the_cap_is_refused()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var tooLong = new string('5', ResidentConfigurationService.MaxEmergencyContactPhoneNumberLength + 1);

        var response = await client.PutAsJsonAsync(
            $"/api/residents/{ResidentA}/settings",
            new { EscalationLimit = (int?)null, EmergencyCallEnabled = false, EmergencyContactPhoneNumber = tooLong });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-CFG-01 a caregiver cannot change a resident's settings")]
    [Trait("Requirement", "REQ-CFG-01")]
    public async Task A_caregiver_cannot_change_a_residents_settings()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, FirstCaregiver);

        var response = await client.PutAsJsonAsync(
            $"/api/residents/{ResidentA}/settings",
            new { EscalationLimit = 1, EmergencyCallEnabled = false, EmergencyContactPhoneNumber = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-CFG-01 a caregiver can read a resident's settings")]
    [Trait("Requirement", "REQ-CFG-01")]
    public async Task A_caregiver_can_read_a_residents_settings()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, FirstCaregiver);

        var settings = await client.GetFromJsonAsync<ResidentSettingsView>(
            $"/api/residents/{ResidentA}/settings");

        Assert.NotNull(settings);
    }

    [Fact(DisplayName = "REQ-CFG-02 a resident account can assign a caregiver by username")]
    [Trait("Requirement", "REQ-CFG-02")]
    public async Task A_resident_account_can_assign_a_caregiver_by_username()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var register = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                UserName = "new.caregiver@example.invalid",
                Password = ElderSafeWebAppFactory.DemoPassword,
                DisplayName = "New Caregiver",
                AccountType = AccountType.Caregiver
            });

        Assert.Equal(HttpStatusCode.NoContent, register.StatusCode);

        var assign = await client.PostAsJsonAsync(
            $"/api/residents/{ResidentA}/caregivers",
            new { UserName = "new.caregiver@example.invalid" });

        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);

        var caregivers = await client.GetFromJsonAsync<List<AssignedCaregiverView>>(
            $"/api/residents/{ResidentA}/caregivers");

        Assert.NotNull(caregivers);
        Assert.Contains(caregivers!, c => c.UserName == "new.caregiver@example.invalid");
    }

    [Fact(DisplayName = "REQ-CFG-02 assigning an unknown caregiver is refused")]
    [Trait("Requirement", "REQ-CFG-02")]
    public async Task Assigning_an_unknown_caregiver_is_refused()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var response = await client.PostAsJsonAsync(
            $"/api/residents/{ResidentA}/caregivers",
            new { UserName = "nobody@example.invalid" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-CFG-07 looking up an existing caregiver's exact email finds their display name")]
    [Trait("Requirement", "REQ-CFG-07")]
    public async Task Looking_up_an_existing_caregivers_exact_email_finds_their_display_name()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var response = await client.GetAsync(
            $"/api/residents/{ResidentA}/caregivers/lookup?email={Uri.EscapeDataString(FirstCaregiver)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CaregiverLookupResult>();

        Assert.NotNull(result);
        Assert.True(result!.Found);
        Assert.False(string.IsNullOrEmpty(result.DisplayName));
    }

    [Fact(DisplayName = "REQ-CFG-07 looking up an unknown email reports not found, with no display name")]
    [Trait("Requirement", "REQ-CFG-07")]
    public async Task Looking_up_an_unknown_email_reports_not_found_with_no_display_name()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var response = await client.GetAsync(
            $"/api/residents/{ResidentA}/caregivers/lookup?email=nobody@example.invalid");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CaregiverLookupResult>();

        Assert.NotNull(result);
        Assert.False(result!.Found);
        Assert.Null(result.DisplayName);
    }

    [Fact(DisplayName = "REQ-CFG-07 a partial email does not match an account registered under a longer one")]
    [Trait("Requirement", "REQ-CFG-07")]
    public async Task A_partial_email_does_not_match_an_account_registered_under_a_longer_one()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var prefix = FirstCaregiver[..^1];

        var response = await client.GetAsync(
            $"/api/residents/{ResidentA}/caregivers/lookup?email={Uri.EscapeDataString(prefix)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<CaregiverLookupResult>();

        Assert.NotNull(result);
        Assert.False(result!.Found);
    }

    [Fact(DisplayName = "REQ-CFG-07 a caregiver cannot use the lookup endpoint")]
    [Trait("Requirement", "REQ-CFG-07")]
    public async Task A_caregiver_cannot_use_the_lookup_endpoint()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, FirstCaregiver);

        var response = await client.GetAsync(
            $"/api/residents/{ResidentA}/caregivers/lookup?email={Uri.EscapeDataString(SecondCaregiver)}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-CFG-03 removing the last caregiver is refused")]
    [Trait("Requirement", "REQ-CFG-03")]
    public async Task Removing_the_last_caregiver_is_refused()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var caregivers = await client.GetFromJsonAsync<List<AssignedCaregiverView>>(
            $"/api/residents/{ResidentA}/caregivers");

        Assert.NotNull(caregivers);

        HttpResponseMessage? last = null;

        foreach (var caregiver in caregivers!)
        {
            last = await client.DeleteAsync(
                $"/api/residents/{ResidentA}/caregivers/{caregiver.CaregiverId}");
        }

        Assert.NotNull(last);
        Assert.Equal(HttpStatusCode.BadRequest, last!.StatusCode);

        var remaining = await client.GetFromJsonAsync<List<AssignedCaregiverView>>(
            $"/api/residents/{ResidentA}/caregivers");

        Assert.NotNull(remaining);
        Assert.Single(remaining!);
    }

    [Fact(DisplayName = "REQ-CFG-08 a resident can reorder its assigned caregivers")]
    [Trait("Requirement", "REQ-CFG-08")]
    public async Task A_resident_can_reorder_its_assigned_caregivers()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var response = await client.PutAsJsonAsync(
            $"/api/residents/{ResidentA}/caregivers/order",
            new { CaregiverIds = new[] { SecondCaregiverId, FirstCaregiverId } });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var caregivers = await client.GetFromJsonAsync<List<AssignedCaregiverView>>(
            $"/api/residents/{ResidentA}/caregivers");

        Assert.NotNull(caregivers);
        Assert.Equal(SecondCaregiverId, caregivers![0].CaregiverId);
        Assert.Equal(0, caregivers[0].Order);
        Assert.Equal(FirstCaregiverId, caregivers[1].CaregiverId);
        Assert.Equal(1, caregivers[1].Order);
    }

    [Fact(DisplayName = "REQ-CFG-08 reordering with a caregiver who is not assigned is refused")]
    [Trait("Requirement", "REQ-CFG-08")]
    public async Task Reordering_with_a_caregiver_who_is_not_assigned_is_refused()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var response = await client.PutAsJsonAsync(
            $"/api/residents/{ResidentA}/caregivers/order",
            new { CaregiverIds = new[] { FirstCaregiverId, Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-CFG-08 reordering that omits a currently assigned caregiver is refused")]
    [Trait("Requirement", "REQ-CFG-08")]
    public async Task Reordering_that_omits_a_currently_assigned_caregiver_is_refused()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var response = await client.PutAsJsonAsync(
            $"/api/residents/{ResidentA}/caregivers/order",
            new { CaregiverIds = new[] { FirstCaregiverId } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var caregivers = await client.GetFromJsonAsync<List<AssignedCaregiverView>>(
            $"/api/residents/{ResidentA}/caregivers");

        Assert.NotNull(caregivers);
        Assert.Equal(2, caregivers!.Count);
        Assert.Equal(FirstCaregiverId, caregivers[0].CaregiverId);
        Assert.Equal(SecondCaregiverId, caregivers[1].CaregiverId);
    }

    [Fact(DisplayName = "REQ-CFG-08 reordering with a duplicate caregiver id is refused")]
    [Trait("Requirement", "REQ-CFG-08")]
    public async Task Reordering_with_a_duplicate_caregiver_id_is_refused()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var response = await client.PutAsJsonAsync(
            $"/api/residents/{ResidentA}/caregivers/order",
            new { CaregiverIds = new[] { FirstCaregiverId, FirstCaregiverId } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-CFG-08 a caregiver cannot reorder a resident's caregivers")]
    [Trait("Requirement", "REQ-CFG-08")]
    public async Task A_caregiver_cannot_reorder_a_residents_caregivers()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, FirstCaregiver);

        var response = await client.PutAsJsonAsync(
            $"/api/residents/{ResidentA}/caregivers/order",
            new { CaregiverIds = new[] { SecondCaregiverId, FirstCaregiverId } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-CFG-04 registering grants no access to another resident's data")]
    [Trait("Requirement", "REQ-CFG-04")]
    public async Task Registering_grants_no_access_to_another_residents_data()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });

        var register = await client.PostAsJsonAsync(
            "/api/auth/register",
            new
            {
                UserName = "stranger@example.invalid",
                Password = ElderSafeWebAppFactory.DemoPassword,
                DisplayName = "Stranger",
                AccountType = AccountType.Caregiver
            });

        Assert.Equal(HttpStatusCode.NoContent, register.StatusCode);

        var signIn = await client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { UserName = "stranger@example.invalid", Password = ElderSafeWebAppFactory.DemoPassword });

        Assert.Equal(HttpStatusCode.NoContent, signIn.StatusCode);

        var alerts = await client.GetAsync($"/api/residents/{ResidentA}/alerts");

        Assert.Equal(HttpStatusCode.NotFound, alerts.StatusCode);
    }

    [Fact(DisplayName = "REQ-CFG-05 a configuration change is recorded in the audit log")]
    [Trait("Requirement", "REQ-CFG-05")]
    public async Task A_configuration_change_is_recorded_in_the_audit_log()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var update = await client.PutAsJsonAsync(
            $"/api/residents/{ResidentA}/settings",
            new { EscalationLimit = (int?)null, EmergencyCallEnabled = true, EmergencyContactPhoneNumber = (string?)null });

        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider
            .GetRequiredService<ElderSafe.Api.Data.ElderSafeDbContext>();

        Assert.Contains(context.AuditEntries, e => e.Action == "ChangedAlertSettings");
    }

    [Fact(DisplayName = "REQ-SEC-01 a resident account can read its own alerts")]
    [Trait("Requirement", "REQ-SEC-01")]
    public async Task A_resident_account_can_read_its_own_alerts()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory, ResidentAccount);

        var response = await client.GetAsync($"/api/residents/{ResidentA}/alerts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
