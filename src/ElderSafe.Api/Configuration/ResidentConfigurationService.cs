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
    int AssignedCaregiverCount);

/// <summary>A caregiver assigned to a resident.</summary>
public sealed record AssignedCaregiverView(
    Guid CaregiverId,
    string DisplayName,
    string UserName,
    int Order);

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
    LimitBelowOne
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
            count);
    }

    public async Task<ConfigurationOutcome> UpdateSettingsAsync(
        Guid residentId,
        Guid actorAccountId,
        int? escalationLimit,
        bool emergencyCallEnabled,
        CancellationToken cancellationToken = default)
    {
        // Null means "every assigned caregiver", which is the default and is
        // allowed. A number below one would mean nobody is ever notified.
        if (escalationLimit is < 1)
        {
            return ConfigurationOutcome.LimitBelowOne;
        }

        var resident = await _context.Residents
            .FirstOrDefaultAsync(r => r.Id == residentId, cancellationToken);

        if (resident is null)
        {
            return ConfigurationOutcome.ResidentNotFound;
        }

        var before = $"limit={Describe(resident.EscalationLimit)}, emergencyCall={resident.EmergencyCallEnabled}";

        resident.EscalationLimit = escalationLimit;
        resident.EmergencyCallEnabled = emergencyCallEnabled;

        var after = $"limit={Describe(escalationLimit)}, emergencyCall={emergencyCallEnabled}";

        await RecordAndAnnounceAsync(
            residentId, actorAccountId, "ChangedAlertSettings", $"{before} -> {after}", cancellationToken);

        return ConfigurationOutcome.Changed;
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
