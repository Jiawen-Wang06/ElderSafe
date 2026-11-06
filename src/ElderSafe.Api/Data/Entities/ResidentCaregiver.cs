namespace ElderSafe.Api.Data.Entities;

/// <summary>
/// Assigns a caregiver to a resident at a position in the escalation order.
/// </summary>
/// <remarks>
/// Modelled explicitly rather than as an implicit many-to-many, because the
/// relationship carries data of its own: <see cref="Order"/> decides who is
/// told first and who is told next. It is also what per-resident authorization
/// is checked against (REQ-SEC-01, RISK-12), so it needs to be queryable
/// directly rather than inferred.
/// </remarks>
public class ResidentCaregiver
{
    public Guid ResidentId { get; set; }

    public Resident Resident { get; set; } = null!;

    public Guid CaregiverId { get; set; }

    public UserAccount Caregiver { get; set; } = null!;

    /// <summary>
    /// Zero-based position in the escalation order. Position 0 is notified
    /// when the alert is raised.
    /// </summary>
    public int Order { get; set; }
}
