using ElderSafe.Api.Alerting;

namespace ElderSafe.Api.Tests.Alerting;

public class EscalationPolicyTests
{
    private static readonly DateTimeOffset RaisedAt = new(2026, 11, 5, 3, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private static EscalationPolicy CreatePolicy()
    {
        return new EscalationPolicy(new EscalationOptions { ResponseTimeout = Timeout });
    }

    private static AlertSnapshot Alert(
        int escalationLevel = 0,
        DateTimeOffset? lastNotifiedAt = null,
        DateTimeOffset? acknowledgedAt = null,
        DateTimeOffset? dispatchedAt = null)
    {
        return new AlertSnapshot(
            Id: Guid.NewGuid(),
            ResidentId: Guid.NewGuid(),
            Kind: AlertKind.Fall,
            RaisedAt: RaisedAt,
            LastNotifiedAt: lastNotifiedAt ?? RaisedAt,
            EscalationLevel: escalationLevel,
            AcknowledgedAt: acknowledgedAt,
            DispatchedAt: dispatchedAt);
    }

    [Fact(DisplayName = "REQ-FALL-03 escalates to the next caregiver once the timeout has passed")]
    [Trait("Requirement", "REQ-FALL-03")]
    public void Escalates_to_the_next_caregiver_once_the_timeout_has_passed()
    {
        var settings = new ResidentAlertSettings(AssignedCaregiverCount: 3);

        var decision = CreatePolicy().Decide(Alert(), settings, RaisedAt + Timeout);

        Assert.Equal(EscalationAction.NotifyCaregiver, decision.Action);
        Assert.Equal(1, decision.CaregiverLevel);
    }

    [Fact(DisplayName = "REQ-FALL-03 does nothing while the current caregiver still has time")]
    [Trait("Requirement", "REQ-FALL-03")]
    public void Does_nothing_while_the_current_caregiver_still_has_time()
    {
        var settings = new ResidentAlertSettings(AssignedCaregiverCount: 3);

        var decision = CreatePolicy().Decide(
            Alert(), settings, RaisedAt + Timeout - TimeSpan.FromSeconds(1));

        Assert.Equal(EscalationAction.None, decision.Action);
    }

    [Fact(DisplayName = "REQ-FALL-03 measures the timeout from the most recent notification")]
    [Trait("Requirement", "REQ-FALL-03")]
    public void Measures_the_timeout_from_the_most_recent_notification()
    {
        var settings = new ResidentAlertSettings(AssignedCaregiverCount: 3);
        var escalatedAt = RaisedAt + Timeout;
        var alert = Alert(escalationLevel: 1, lastNotifiedAt: escalatedAt);

        var decision = CreatePolicy().Decide(
            alert, settings, escalatedAt + Timeout - TimeSpan.FromSeconds(1));

        Assert.Equal(EscalationAction.None, decision.Action);
    }

    [Fact(DisplayName = "REQ-FALL-03 never escalates an acknowledged alert however long has passed")]
    [Trait("Requirement", "REQ-FALL-03")]
    public void Never_escalates_an_acknowledged_alert_however_long_has_passed()
    {
        var settings = new ResidentAlertSettings(AssignedCaregiverCount: 3);
        var alert = Alert(acknowledgedAt: RaisedAt + TimeSpan.FromSeconds(30));

        var decision = CreatePolicy().Decide(alert, settings, RaisedAt + TimeSpan.FromHours(12));

        Assert.Equal(EscalationAction.None, decision.Action);
    }

    [Fact(DisplayName = "REQ-FALL-04 escalates through every assigned caregiver by default")]
    [Trait("Requirement", "REQ-FALL-04")]
    public void Escalates_through_every_assigned_caregiver_by_default()
    {
        var settings = new ResidentAlertSettings(AssignedCaregiverCount: 3);

        var decision = CreatePolicy().Decide(
            Alert(escalationLevel: 1), settings, RaisedAt + Timeout);

        Assert.Equal(EscalationAction.NotifyCaregiver, decision.Action);
        Assert.Equal(2, decision.CaregiverLevel);
    }

    [Fact(DisplayName = "REQ-FALL-04 stops escalating at the configured limit")]
    [Trait("Requirement", "REQ-FALL-04")]
    public void Stops_escalating_at_the_configured_limit()
    {
        var settings = new ResidentAlertSettings(AssignedCaregiverCount: 3, EscalationLimit: 2);

        var decision = CreatePolicy().Decide(
            Alert(escalationLevel: 1), settings, RaisedAt + Timeout);

        Assert.Equal(EscalationAction.None, decision.Action);
    }

    [Fact(DisplayName = "REQ-FALL-04 treats a limit below one as one")]
    [Trait("Requirement", "REQ-FALL-04")]
    public void Treats_a_limit_below_one_as_one()
    {
        var settings = new ResidentAlertSettings(AssignedCaregiverCount: 3, EscalationLimit: 0);

        Assert.Equal(1, settings.EffectiveEscalationLimit);

        var decision = CreatePolicy().Decide(Alert(), settings, RaisedAt + Timeout);

        Assert.Equal(EscalationAction.None, decision.Action);
    }

    [Fact(DisplayName = "REQ-FALL-04 treats a limit above the caregiver count as the caregiver count")]
    [Trait("Requirement", "REQ-FALL-04")]
    public void Treats_a_limit_above_the_caregiver_count_as_the_caregiver_count()
    {
        var settings = new ResidentAlertSettings(AssignedCaregiverCount: 2, EscalationLimit: 9);

        Assert.Equal(2, settings.EffectiveEscalationLimit);

        var decision = CreatePolicy().Decide(
            Alert(escalationLevel: 1), settings, RaisedAt + Timeout);

        Assert.Equal(EscalationAction.None, decision.Action);
    }

    [Fact(DisplayName = "REQ-FALL-05 does not place an emergency call when the setting is off")]
    [Trait("Requirement", "REQ-FALL-05")]
    public void Does_not_place_an_emergency_call_when_the_setting_is_off()
    {
        var settings = new ResidentAlertSettings(
            AssignedCaregiverCount: 1,
            EmergencyCallEnabled: false);

        var decision = CreatePolicy().Decide(
            Alert(), settings, RaisedAt + TimeSpan.FromHours(1));

        Assert.Equal(EscalationAction.None, decision.Action);
    }

    [Fact(DisplayName = "REQ-FALL-06 places an emergency call when the chain is exhausted and the setting is on")]
    [Trait("Requirement", "REQ-FALL-06")]
    public void Places_an_emergency_call_when_the_chain_is_exhausted_and_the_setting_is_on()
    {
        var settings = new ResidentAlertSettings(
            AssignedCaregiverCount: 2,
            EmergencyCallEnabled: true);

        var decision = CreatePolicy().Decide(
            Alert(escalationLevel: 1), settings, RaisedAt + Timeout);

        Assert.Equal(EscalationAction.PlaceEmergencyCall, decision.Action);
    }

    [Fact(DisplayName = "REQ-FALL-06 never places a second emergency call for the same alert")]
    [Trait("Requirement", "REQ-FALL-06")]
    public void Never_places_a_second_emergency_call_for_the_same_alert()
    {
        var settings = new ResidentAlertSettings(
            AssignedCaregiverCount: 1,
            EmergencyCallEnabled: true);

        var alert = Alert(dispatchedAt: RaisedAt + Timeout);

        var decision = CreatePolicy().Decide(alert, settings, RaisedAt + TimeSpan.FromHours(1));

        Assert.Equal(EscalationAction.None, decision.Action);
    }

    [Fact(DisplayName = "REQ-FALL-06 treats a resident with no assigned caregivers as an exhausted chain")]
    [Trait("Requirement", "REQ-FALL-06")]
    public void Treats_a_resident_with_no_assigned_caregivers_as_an_exhausted_chain()
    {
        var settings = new ResidentAlertSettings(
            AssignedCaregiverCount: 0,
            EmergencyCallEnabled: true);

        var decision = CreatePolicy().Decide(Alert(), settings, RaisedAt + Timeout);

        Assert.Equal(EscalationAction.PlaceEmergencyCall, decision.Action);
    }
}
