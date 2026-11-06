namespace ElderSafe.Api.Data.Entities;

/// <summary>
/// A wearable paired to one resident.
/// </summary>
/// <remarks>
/// The pairing is what makes the readings endpoint safe. A device
/// authenticates as itself and the resident is read from here, so a device
/// credential can never be used to post readings for somebody else
/// (REQ-SEC-04, RISK-15).
/// </remarks>
public class Device
{
    public Guid Id { get; set; }

    /// <summary>A human-readable label, for example "Band 1".</summary>
    public string Name { get; set; } = string.Empty;

    public Guid ResidentId { get; set; }

    public Resident Resident { get; set; } = null!;

    /// <summary>
    /// SHA-256 of the device key. The key itself is never stored, so a copy of
    /// the database does not hand over the ability to post readings.
    /// </summary>
    public string KeyHash { get; set; } = string.Empty;

    public DateTimeOffset PairedAt { get; set; }

    /// <summary>
    /// Set when the device is retired or its key is believed compromised. A
    /// revoked device authenticates no further.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }
}
