using ElderSafe.Api.Alerting;
using ElderSafe.Api.Data;
using ElderSafe.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ElderSafe.Api.Configuration;

/// <summary>A resident's alerting configuration, as shown and edited.</summary>
public sealed record ResidentSettingsView(
    Guid ResidentId,
    string DisplayName,
    int? EscalationLimit,
    bool EmergencyCallEnabled,
    string? EmergencyContactPhoneNumber,
    int AssignedCaregiverCount);

/// <summary>A caregiver assigned to a resident.</summary>
public sealed record AssignedCaregiverView(
    Guid CaregiverId,
    string DisplayName,
    string UserName,
    int Order);

/// <summary>
/// Whether an exact-match caregiver email lookup found an account
/// (REQ-CFG-07).
/// </summary>
/// <remarks>
/// Deliberately thin: only whether the exact string matches and, if so, the
/// caregiver's display name. No partial matches and no list of candidates,
/// so the lookup cannot be used to enumerate registered caregivers by typing
/// short prefixes (RISK-19) — it answers only the one email actually typed.
/// </remarks>
public sealed record CaregiverLookupResult(bool Found, string? DisplayName);

/// <summary>What a configuration change produced.</summary>
public enum ConfigurationOutcome
{
    Changed,
    ResidentNotFound,
    CaregiverNotFound,
    AlreadyAssigned,

    /// <summary>
    /// Refused because it would leave the resident with no caregivers.
    /// </summary>
    WouldLeaveNoCaregivers,

    /// <summary>Refused because the escalation limit was below one.</summary>
    LimitBelowOne,

    /// <summary>Refused because the escalation limit was above the cap.</summary>
    LimitAboveMax,

    /// <summary>Refused because the emergency contact number was too long.</summary>
    EmergencyContactPhoneNumberTooLong
}

/// <summary>
/// Reads and changes a resident's configuration.
/// </summary>
/// <remarks>
/// <para>
/// The rules here are risk controls, not validation niceties. A resident
/// account can reduce the escalation limit, switch off emergency calling and
/// remove caregivers, which means the person the product protects can weaken
/// their own protection — and for a resident living with dementia that is a
/// realistic failure, not a hypothetical one.
/// </para>
/// <para>
/// ISO 14971 ranks instructions for use as the weakest kind of control, to be
/// relied on only when design cannot do better. The instructions do say a
/// caregiver should register and configure the resident's account. These
/// limits exist so that instruction is not the only thing standing in the
/// way: the last caregiver cannot be removed, the limit cannot go below one,
/// and every change is recorded and pushed to the caregivers who are left.
/// </para>
/// </remarks>
public sealed class ResidentConfigurationService
{
    /// <summary>
    /// The highest escalation limit a resident may set. A sanity bound on
    /// the field, not a risk control: a higher limit does not itself weaken
    /// the resident's protection the way a lower one does.
    /// </summary>
    public const int MaxEscalationLimit = 10;

    /// <summary>
    /// The longest emergency contact number accepted. The field is free text
    /// shown back to the resident and carried into a log line, never dialed,
    /// so this exists only to keep the stored value sane, not to enforce any
    /// particular phone-number format.
    /// </summary>
    public const int MaxEmergencyContactPhoneNumberLength = 32;

    private readonly ElderSafeDbContext _context;
    private readonly IAlertNotifier _notifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ResidentConfigurationService> _logger;

    public ResidentConfigurationService(
        ElderSafeDbContext context,
        IAlertNotifier notifier,
        TimeProvider timeProvider,
        ILogger<ResidentConfigurationService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ResidentSettingsView?> GetSettingsAsync(
        Guid residentId,
        CancellationToken cancellationToken = default)
    {
        var resident = await _context.Residents
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == residentId, cancellationToken);

        if (resident is null)
        {
            return null;
        }

        var count = await _context.ResidentCaregivers
            .CountAsync(rc => rc.ResidentId == residentId, cancellationToken);

        return new ResidentSettingsView(
            resident.Id,
            resident.DisplayName,
            resident.EscalationLimit,
            resident.EmergencyCallEnabled,
            resident.EmergencyContactPhoneNumber,
            count);
    }

    public async Task<ConfigurationOutcome> UpdateSettingsAsync(
        Guid residentId,
        Guid actorAccountId,
        int? escalationLimit,
        bool emergencyCallEnabled,
        string? emergencyContactPhoneNumber,
        CancellationToken cancellationToken = default)
    {
        // Null means "every assigned caregiver", which is the default and is
        // allowed. A number below one would mean nobody is ever notified; a
        // number above the cap is a data-sanity problem, not a safety one
        // (MaxEscalationLimit).
        if (escalationLimit is < 1)
        {
            return ConfigurationOutcome.LimitBelowOne;
        }

        if (escalationLimit > MaxEscalationLimit)
        {
            return ConfigurationOutcome.LimitAboveMax;
        }

        // Blank input means "not configured", stored as null rather than an
        // empty string, so GetSettingsAsync and the frontend have one way to
        // ask "is anything set" instead of two.
        var normalizedPhoneNumber = string.IsNullOrWhiteSpace(emergencyContactPhoneNumber)
            ? null
            : emergencyContactPhoneNumber.Trim();

        if (normalizedPhoneNumber is { Length: > MaxEmergencyContactPhoneNumberLength })
        {
            return ConfigurationOutcome.EmergencyContactPhoneNumberTooLong;
        }

        var resident = await _context.Residents
            .FirstOrDefaultAsync(r => r.Id == residentId, cancellationToken);

        if (resident is null)
        {
            return ConfigurationOutcome.ResidentNotFound;
        }

        var before =
            $"limit={Describe(resident.EscalationLimit)}, emergencyCall={resident.EmergencyCallEnabled}, " +
            $"emergencyContact={Describe(resident.EmergencyContactPhoneNumber)}";

        resident.EscalationLimit = escalationLimit;
        resident.EmergencyCallEnabled = emergencyCallEnabled;
        resident.EmergencyContactPhoneNumber = normalizedPhoneNumber;

        var after =
            $"limit={Describe(escalationLimit)}, emergencyCall={emergencyCallEnabled}, " +
            $"emergencyContact={Describe(normalizedPhoneNumber)}";

        await RecordAndAnnounceAsync(
            residentId, actorAccountId, "ChangedAlertSettings", $"{before} -> {after}", cancellationToken);

        return ConfigurationOutcome.Changed;
    }

    /// <summary>
    /// Checks for a caregiver account with exactly this email (REQ-CFG-07).
    /// </summary>
    /// <remarks>
    /// Scoped to the same authorization as every other change here even
    /// though it changes nothing: it exists only to support adding a
    /// caregiver, so it is gated the same way adding one is, rather than
    /// being reachable by any authenticated account as a general-purpose
    /// directory lookup (RISK-19).
    /// </remarks>
    public async Task<CaregiverLookupResult> LookupCaregiverAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalized = email.Trim().ToUpperInvariant();

        var caregiver = await _context.Users
            .Where(u => u.NormalizedUserName == normalized && u.AccountType == AccountType.Caregiver)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);

        return caregiver is null
            ? new CaregiverLookupResult(false, null)
            : new CaregiverLookupResult(true, caregiver);
    }

    public async Task<IReadOnlyList<AssignedCaregiverView>?> GetCaregiversAsync(
        Guid residentId,
        CancellationToken cancellationToken = default)
    {
        var exists = await _context.Residents
            .AnyAsync(r => r.Id == residentId, cancellationToken);

        if (!exists)
        {
            return null;
        }

        return await _context.ResidentCaregivers
            .Where(rc => rc.ResidentId == residentId)
            .OrderBy(rc => rc.Order)
            .Select(rc => new AssignedCaregiverView(
                rc.CaregiverId,
                rc.Caregiver.DisplayName,
                rc.Caregiver.UserName ?? string.Empty,
                rc.Order))
            .ToListAsync(cancellationToken);
    }

    public async Task<ConfigurationOutcome> AssignCaregiverAsync(
        Guid residentId,
        Guid actorAccountId,
        string caregiverUserName,
        CancellationToken cancellationToken = default)
    {
        var resident = await _context.Residents
            .AnyAsync(r => r.Id == residentId, cancellationToken);

        if (!resident)
        {
            return ConfigurationOutcome.ResidentNotFound;
        }

        var normalized = caregiverUserName.ToUpperInvariant();

        var caregiver = await _context.Users
            .FirstOrDefaultAsync(
                u => u.NormalizedUserName == normalized && u.AccountType == AccountType.Caregiver,
                cancellationToken);

        if (caregiver is null)
        {
            return ConfigurationOutcome.CaregiverNotFound;
        }

        var already = await _context.ResidentCaregivers
            .AnyAsync(rc => rc.ResidentId == residentId && rc.CaregiverId == caregiver.Id, cancellationToken);

        if (already)
        {
            return ConfigurationOutcome.AlreadyAssigned;
        }

        var nextOrder = await _context.ResidentCaregivers
            .Where(rc => rc.ResidentId == residentId)
            .Select(rc => (int?)rc.Order)
            .MaxAsync(cancellationToken) ?? -1;

        _context.ResidentCaregivers.Add(new ResidentCaregiver
        {
            ResidentId = residentId,
            CaregiverId = caregiver.Id,
            Order = nextOrder + 1
        });

        await RecordAndAnnounceAsync(
            residentId, actorAccountId, "AssignedCaregiver", caregiver.UserName ?? caregiver.Id.ToString(), cancellationToken);

        return ConfigurationOutcome.Changed;
    }

    public async Task<ConfigurationOutcome> RemoveCaregiverAsync(
        Guid residentId,
        Guid actorAccountId,
        Guid caregiverId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await _context.ResidentCaregivers
            .FirstOrDefaultAsync(
                rc => rc.ResidentId == residentId && rc.CaregiverId == caregiverId, cancellationToken);

        if (assignment is null)
        {
            return ConfigurationOutcome.CaregiverNotFound;
        }

        var remaining = await _context.ResidentCaregivers
            .CountAsync(rc => rc.ResidentId == residentId, cancellationToken);

        // A resident with no caregivers has no safety net at all: a fall
        // raises an alert that reaches nobody. Made impossible rather than
        // discouraged (RISK-07).
        if (remaining <= 1)
        {
            return ConfigurationOutcome.WouldLeaveNoCaregivers;
        }

        _context.ResidentCaregivers.Remove(assignment);

        await RecordAndAnnounceAsync(
            residentId, actorAccountId, "RemovedCaregiver", caregiverId.ToString(), cancellationToken);

        return ConfigurationOutcome.Changed;
    }

    private static string Describe(int? limit)
    {
        if (limit is null)
        {
            return "all caregivers";
        }

        return limit.Value.ToString();
    }

    private static string Describe(string? emergencyContactPhoneNumber)
    {
        return emergencyContactPhoneNumber ?? "not configured";
    }

    /// <summary>
    /// Saves the change, records who made it, and tells the caregivers.
    /// </summary>
    /// <remarks>
    /// Announcing is the protective measure that stops a weakening change
    /// being silent: a caregiver who is removed, or who is left when
    /// emergency calling is switched off, finds out.
    /// </remarks>
    private async Task RecordAndAnnounceAsync(
        Guid residentId,
        Guid actorAccountId,
        string action,
        string detail,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        _context.AuditEntries.Add(new AuditEntry
        {
            Id = Guid.NewGuid(),
            At = now,
            ActorCaregiverId = actorAccountId,
            Action = action,
            ResidentId = residentId,
            Detail = detail
        });

        await _context.SaveChangesAsync(cancellationToken);

        try
        {
            await _notifier.ConfigurationChangedAsync(
                new ResidentConfigurationChange(residentId, action, detail, now), cancellationToken);
        }
        catch (Exception exception)
        {
            // The change is saved; a failed announcement must not undo it.
            _logger.LogError(
                exception, "Could not announce configuration change for resident {ResidentId}", residentId);
        }
    }
}
