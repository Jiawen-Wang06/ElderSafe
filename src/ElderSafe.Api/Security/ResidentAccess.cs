using ElderSafe.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace ElderSafe.Api.Security;

/// <summary>
/// Whether a caregiver may see a resident (REQ-SEC-01).
/// </summary>
/// <remarks>
/// Pulled out of <c>AlertService</c> because the SignalR hub needs the same
/// answer, and a second copy of this rule is a second place for it to be
/// wrong. Signing in proves identity; this is what grants access (RISK-12).
/// </remarks>
public interface IResidentAccess
{
    Task<bool> IsAssignedAsync(
        Guid caregiverId,
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

    public Task<bool> IsAssignedAsync(
        Guid caregiverId,
        Guid residentId,
        CancellationToken cancellationToken = default)
    {
        return _context.ResidentCaregivers.AnyAsync(
            rc => rc.CaregiverId == caregiverId && rc.ResidentId == residentId,
            cancellationToken);
    }
}
