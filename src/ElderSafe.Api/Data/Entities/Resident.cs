namespace ElderSafe.Api.Data.Entities;

/// <summary>
/// A monitored older adult.
/// </summary>
public class Resident
{
    public Guid Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Centre of the safe zone. Null when no zone has been configured yet,
    /// which is a real state: wandering cannot be evaluated until an
    /// authorized user sets one (REQ-WANDER-07).
    /// </summary>
    public double? SafeZoneLatitude { get; set; }

    public double? SafeZoneLongitude { get; set; }

    public double? SafeZoneRadiusMeters { get; set; }

    /// <summary>
    /// How many caregivers an alert may travel through (REQ-FALL-04). Null
    /// means every assigned caregiver, which is the default.
    /// </summary>
    public int? EscalationLimit { get; set; }

    /// <summary>
    /// Whether an emergency call follows an exhausted escalation chain
    /// (REQ-FALL-05). Off by default: an unwanted call is RISK-09.
    /// </summary>
    public bool EmergencyCallEnabled { get; set; }

    /// <summary>Caregivers assigned to this resident, in escalation order.</summary>
    public ICollection<ResidentCaregiver> Caregivers { get; set; } = new List<ResidentCaregiver>();
}
