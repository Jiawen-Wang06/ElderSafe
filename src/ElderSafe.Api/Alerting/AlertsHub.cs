using System.Security.Claims;
using ElderSafe.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ElderSafe.Api.Alerting;

/// <summary>
/// The connection caregivers hold open to receive alerts.
/// </summary>
/// <remarks>
/// Clients subscribe per resident, and a subscription is checked against the
/// same assignment rule the HTTP endpoints use. Without that check, any
/// signed-in caregiver could join any resident's group and receive alerts for
/// people they have no relationship with, which is RISK-12 arriving over a
/// different transport.
/// </remarks>
[Authorize]
public sealed class AlertsHub : Hub
{
    private readonly IResidentAccess _residentAccess;

    public AlertsHub(IResidentAccess residentAccess)
    {
        _residentAccess = residentAccess ?? throw new ArgumentNullException(nameof(residentAccess));
    }

    /// <summary>The group alerts about one resident are sent to.</summary>
    public static string ResidentGroup(Guid residentId)
    {
        return $"resident:{residentId}";
    }

    /// <summary>
    /// Starts receiving alerts about a resident.
    /// </summary>
    /// <returns>False if the caregiver is not assigned to that resident.</returns>
    public async Task<bool> SubscribeToResident(Guid residentId)
    {
        var caregiverId = CurrentCaregiverId();

        if (caregiverId is null)
        {
            return false;
        }

        if (!await _residentAccess.IsAssignedAsync(caregiverId.Value, residentId, Context.ConnectionAborted))
        {
            return false;
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId, ResidentGroup(residentId), Context.ConnectionAborted);

        return true;
    }

    /// <summary>Stops receiving alerts about a resident.</summary>
    public Task UnsubscribeFromResident(Guid residentId)
    {
        return Groups.RemoveFromGroupAsync(
            Context.ConnectionId, ResidentGroup(residentId), Context.ConnectionAborted);
    }

    private Guid? CurrentCaregiverId()
    {
        var value = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);

        if (Guid.TryParse(value, out var id))
        {
            return id;
        }

        return null;
    }
}
