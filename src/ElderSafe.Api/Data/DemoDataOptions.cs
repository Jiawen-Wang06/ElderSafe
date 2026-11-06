namespace ElderSafe.Api.Data;

/// <summary>
/// Controls the demo data used to make the application explorable.
/// </summary>
public sealed class DemoDataOptions
{
    public const string SectionName = "DemoData";

    /// <summary>
    /// Whether to seed demo residents and caregivers. Off by default, so a
    /// deployment that is not a demo never grows invented residents, and the
    /// published demo credentials in the README never exist anywhere else
    /// (ADR-006).
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Password given to every seeded demo caregiver.
    /// </summary>
    /// <remarks>
    /// Published in the README, which is only acceptable because every seeded
    /// record is invented and the demo holds nothing real. Seeding is off by
    /// default so these credentials exist nowhere else (ADR-006).
    /// </remarks>
    public string CaregiverPassword { get; set; } = "Demo-Passw0rd!";

    /// <summary>
    /// Key given to the seeded demo device.
    /// </summary>
    /// <remarks>
    /// A real device key is generated at pairing and shown once. This one is
    /// fixed so the demo and its tests can use it, which is acceptable only
    /// because seeding is off by default and the demo holds nothing real.
    /// </remarks>
    public string DeviceKey { get; set; } = "demo-device-key-not-for-real-use";
}
