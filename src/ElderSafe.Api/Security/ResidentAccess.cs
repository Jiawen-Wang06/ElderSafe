using ElderSafe.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ElderSafe.Api.Security;

/// <summary>
/// What an account may do with a resident (REQ-SEC-01).
/// </summary>
/// <remarks>
/// One place for both questions, because the HTTP endpoints, the SignalR hub
/// and the configuration endpoints all need the same answers, and a second
/// copy of either rule is a second place for it to be wrong (RISK-12).
/// </remarks>
public interface IResidentAccess
{
    /// <summary>
    /// Whether this account may see the resident's alerts and data: the
    /// resident's own account, or a caregiver assigned to them.
    /// </summary>
    Task<bool> CanViewAsync(
        Guid accountId,
        Guid residentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether this account may change the resident's configuration: the
    /// resident's own account only.
    /// </summary>
    /// <remarks>
    /// Caregivers read, they do not configure. A caregiver who could reduce
    /// the escalation limit could remove themselves from the chain without
    /// the resident knowing.
    /// </remarks>
    Task<bool> CanConfigureAsync(
        Guid accountId,
        Guid residentId,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ResidentAccess : IResidentAccess
{
    private readonly ElderSafeDbContext _context;

    public ResidentAccess(ElderSafeDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<bool> CanViewAsync(
        Guid accountId,
        Guid residentId,
        CancellationToken cancellationToken = default)
    {
        if (await CanConfigureAsync(accountId, residentId, cancellationToken))
        {
            return true;
        }

        return await _context.ResidentCaregivers.AnyAsync(
            rc => rc.CaregiverId == accountId && rc.ResidentId == residentId,
            cancellationToken);
    }

    public Task<bool> CanConfigureAsync(
        Guid accountId,
        Guid residentId,
        CancellationToken cancellationToken = default)
    {
        return _context.Residents.AnyAsync(
            r => r.Id == residentId && r.AccountId == accountId,
            cancellationToken);
    }
}
