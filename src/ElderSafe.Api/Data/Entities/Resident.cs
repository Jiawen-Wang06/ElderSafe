namespace ElderSafe.Api.Data.Entities;

/// <summary>
/// A monitored older adult.
/// </summary>
public class Resident
{
    public Guid Id { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The resident account that holds and configures this resident's data.
    /// </summary>
    /// <remarks>
    /// Null for a resident created by seeding before any account existed.
    /// Only this account may change the settings below; caregivers read, they
    /// do not configure.
    /// </remarks>
    public Guid? AccountId { get; set; }

    public UserAccount? Account { get; set; }

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
    /// means every assigned caregiver, which is the default. Bounded to at
    /// most 10 (REQ-CFG-01) as a data-sanity cap on this field, not a safety
    /// control: a higher limit does not itself weaken the resident's
    /// protection the way a lower one does.
    /// </summary>
    public int? EscalationLimit { get; set; }

    /// <summary>
    /// Whether an emergency call follows an exhausted escalation chain
    /// (REQ-FALL-05). Off by default: an unwanted call is RISK-09.
    /// </summary>
    public bool EmergencyCallEnabled { get; set; }

    /// <summary>
    /// A phone number the resident associates with the emergency-call
    /// setting (REQ-CFG-06). Null when not configured.
    /// </summary>
    /// <remarks>
    /// Stored and shown for the resident's own reference, and carried into
    /// the simulated dispatch log entry. It is never passed to anything
    /// capable of placing a real call: the project's only dispatch
    /// implementation, <see cref="Alerting.LoggingEmergencyDispatch"/>, only
    /// logs, and that does not change with this field. A resident who has
    /// not set this sees it reported as not configured rather than falling
    /// back to a displayed number, since this project must never present a
    /// real emergency number as something it would actually dial.
    /// </remarks>
    public string? EmergencyContactPhoneNumber { get; set; }

    /// <summary>Caregivers assigned to this resident, in escalation order.</summary>
    public ICollection<ResidentCaregiver> Caregivers { get; set; } = new List<ResidentCaregiver>();
}
