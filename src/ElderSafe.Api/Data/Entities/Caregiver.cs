using Microsoft.AspNetCore.Identity;

namespace ElderSafe.Api.Data.Entities;

/// <summary>
/// A person who receives and acknowledges alerts.
/// </summary>
/// <remarks>
/// Derives from <see cref="IdentityUser{TKey}"/> rather than sitting in a
/// separate table joined to one (ADR-006). The caregiver *is* the
/// authenticated user, so there is no identity-to-domain mapping to keep in
/// step, and a caregiver's resident assignments hang directly off the signed-in
/// principal.
/// </remarks>
public class Caregiver : IdentityUser<Guid>
{
    /// <summary>The name shown on the dashboard and in the audit log.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The residents this caregiver is assigned to.</summary>
    public ICollection<ResidentCaregiver> Assignments { get; set; } = new List<ResidentCaregiver>();
}
