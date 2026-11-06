using Microsoft.AspNetCore.Identity;

namespace ElderSafe.Api.Data.Entities;

/// <summary>
/// What an account is for.
/// </summary>
public enum AccountType
{
    /// <summary>
    /// Holds a resident's data and configures it.
    /// </summary>
    /// <remarks>
    /// Named for whose data it holds, not for who operates it. The
    /// instructions say a caregiver registers the resident and configures the
    /// account, and that a resident living with dementia should not be given
    /// the dashboard. That instruction is the weakest tier of risk control
    /// under ISO 14971, which is why the design-level limits exist as well:
    /// the last caregiver cannot be removed and the escalation limit cannot
    /// go below one.
    /// </remarks>
    Resident,

    /// <summary>Receives and acknowledges alerts for assigned residents.</summary>
    Caregiver
}

/// <summary>
/// A person who can sign in.
/// </summary>
/// <remarks>
/// One account type, because ASP.NET Core Identity supports one. What an
/// account may do comes from <see cref="AccountType"/> and from the
/// relationships it has to residents, never from the account alone:
/// registering proves identity and grants access to nothing.
/// </remarks>
public class UserAccount : IdentityUser<Guid>
{
    /// <summary>The name shown on the dashboard and in the audit log.</summary>
    public string DisplayName { get; set; } = string.Empty;

    public AccountType AccountType { get; set; }

    /// <summary>Residents this account is assigned to as a caregiver.</summary>
    public ICollection<ResidentCaregiver> Assignments { get; set; } = new List<ResidentCaregiver>();
}
