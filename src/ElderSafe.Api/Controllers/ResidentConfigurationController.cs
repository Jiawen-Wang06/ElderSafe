using ElderSafe.Api.Configuration;
using ElderSafe.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ElderSafe.Api.Controllers;

/// <summary>New values for a resident's alerting configuration.</summary>
/// <param name="EscalationLimit">
/// How many caregivers an alert travels through. Null means all of them.
/// </param>
/// <param name="EmergencyCallEnabled">
/// Whether an emergency call follows an exhausted escalation chain.
/// </param>
/// <param name="EmergencyContactPhoneNumber">
/// A phone number the resident associates with the emergency-call setting.
/// Null or blank means not configured. Stored and shown back to the
/// resident only — never passed to anything capable of placing a call.
/// </param>
public sealed record UpdateSettingsRequest(
    int? EscalationLimit,
    bool EmergencyCallEnabled,
    string? EmergencyContactPhoneNumber);

/// <summary>The caregiver to assign, by the username they registered with.</summary>
public sealed record AssignCaregiverRequest(string UserName);

/// <summary>
/// The resident's assigned caregivers, in the escalation order they should
/// now hold.
/// </summary>
/// <param name="CaregiverIds">
/// Every currently assigned caregiver's id, each exactly once, in the new
/// order. Not a way to add or remove one — see
/// <see cref="ResidentConfigurationService.ReorderCaregiversAsync"/>.
/// </param>
public sealed record ReorderCaregiversRequest(IReadOnlyList<Guid> CaregiverIds);

/// <summary>
/// Reads and changes a resident's configuration.
/// </summary>
/// <remarks>
/// Only the resident's own account may change anything here. Caregivers read
/// their assigned residents; they do not configure them, because a caregiver
/// able to reduce the escalation limit could remove themselves from the chain
/// without the resident knowing.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/residents/{residentId:guid}")]
public sealed class ResidentConfigurationController : ControllerBase
{
    private readonly ResidentConfigurationService _configuration;
    private readonly IResidentAccess _residentAccess;
    private readonly ICurrentCaregiver _currentAccount;

    public ResidentConfigurationController(
        ResidentConfigurationService configuration,
        IResidentAccess residentAccess,
        ICurrentCaregiver currentAccount)
    {
        _configuration = configuration;
        _residentAccess = residentAccess;
        _currentAccount = currentAccount;
    }

    [HttpGet("settings")]
    public async Task<ActionResult<ResidentSettingsView>> GetSettings(
        Guid residentId,
        CancellationToken cancellationToken)
    {
        var accountId = _currentAccount.Id;

        if (accountId is null)
        {
            return Unauthorized();
        }

        // Readable by anyone who may view the resident, so a caregiver can see
        // whether emergency calling is on without being able to change it.
        if (!await _residentAccess.CanViewAsync(accountId.Value, residentId, cancellationToken))
        {
            return NotFound();
        }

        var settings = await _configuration.GetSettingsAsync(residentId, cancellationToken);

        if (settings is null)
        {
            return NotFound();
        }

        return Ok(settings);
    }

    [HttpPut("settings")]
    public async Task<IActionResult> UpdateSettings(
        Guid residentId,
        [FromBody] UpdateSettingsRequest request,
        CancellationToken cancellationToken)
    {
        var accountId = await RequireConfigureAsync(residentId, cancellationToken);

        if (accountId is null)
        {
            return NotFound();
        }

        var outcome = await _configuration.UpdateSettingsAsync(
            residentId,
            accountId.Value,
            request.EscalationLimit,
            request.EmergencyCallEnabled,
            request.EmergencyContactPhoneNumber,
            cancellationToken);

        return FromOutcome(outcome);
    }

    /// <summary>
    /// Checks for a caregiver account with exactly this email, for the
    /// "add a caregiver" form to confirm before submitting (REQ-CFG-07).
    /// </summary>
    /// <remarks>
    /// Gated the same way assigning a caregiver is, rather than being open
    /// to any authenticated account: this is part of that configuration
    /// flow, not a general-purpose account directory (RISK-19).
    /// </remarks>
    [HttpGet("caregivers/lookup")]
    public async Task<ActionResult<CaregiverLookupResult>> LookupCaregiver(
        Guid residentId,
        [FromQuery] string email,
        CancellationToken cancellationToken)
    {
        var accountId = await RequireConfigureAsync(residentId, cancellationToken);

        if (accountId is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return Ok(new CaregiverLookupResult(false, null));
        }

        var result = await _configuration.LookupCaregiverAsync(email, cancellationToken);

        return Ok(result);
    }

    [HttpGet("caregivers")]
    public async Task<ActionResult<IReadOnlyList<AssignedCaregiverView>>> GetCaregivers(
        Guid residentId,
        CancellationToken cancellationToken)
    {
        var accountId = _currentAccount.Id;

        if (accountId is null)
        {
            return Unauthorized();
        }

        if (!await _residentAccess.CanViewAsync(accountId.Value, residentId, cancellationToken))
        {
            return NotFound();
        }

        var caregivers = await _configuration.GetCaregiversAsync(residentId, cancellationToken);

        if (caregivers is null)
        {
            return NotFound();
        }

        return Ok(caregivers);
    }

    [HttpPost("caregivers")]
    public async Task<IActionResult> AssignCaregiver(
        Guid residentId,
        [FromBody] AssignCaregiverRequest request,
        CancellationToken cancellationToken)
    {
        var accountId = await RequireConfigureAsync(residentId, cancellationToken);

        if (accountId is null)
        {
            return NotFound();
        }

        var outcome = await _configuration.AssignCaregiverAsync(
            residentId, accountId.Value, request.UserName, cancellationToken);

        return FromOutcome(outcome);
    }

    [HttpDelete("caregivers/{caregiverId:guid}")]
    public async Task<IActionResult> RemoveCaregiver(
        Guid residentId,
        Guid caregiverId,
        CancellationToken cancellationToken)
    {
        var accountId = await RequireConfigureAsync(residentId, cancellationToken);

        if (accountId is null)
        {
            return NotFound();
        }

        var outcome = await _configuration.RemoveCaregiverAsync(
            residentId, accountId.Value, caregiverId, cancellationToken);

        return FromOutcome(outcome);
    }

    [HttpPut("caregivers/order")]
    public async Task<IActionResult> ReorderCaregivers(
        Guid residentId,
        [FromBody] ReorderCaregiversRequest request,
        CancellationToken cancellationToken)
    {
        var accountId = await RequireConfigureAsync(residentId, cancellationToken);

        if (accountId is null)
        {
            return NotFound();
        }

        var outcome = await _configuration.ReorderCaregiversAsync(
            residentId, accountId.Value, request.CaregiverIds, cancellationToken);

        return FromOutcome(outcome);
    }

    private async Task<Guid?> RequireConfigureAsync(Guid residentId, CancellationToken cancellationToken)
    {
        var accountId = _currentAccount.Id;

        if (accountId is null)
        {
            return null;
        }

        if (!await _residentAccess.CanConfigureAsync(accountId.Value, residentId, cancellationToken))
        {
            return null;
        }

        return accountId;
    }

    private IActionResult FromOutcome(ConfigurationOutcome outcome)
    {
        if (outcome == ConfigurationOutcome.Changed)
        {
            return NoContent();
        }

        if (outcome is ConfigurationOutcome.ResidentNotFound or ConfigurationOutcome.CaregiverNotFound)
        {
            return NotFound();
        }

        if (outcome == ConfigurationOutcome.AlreadyAssigned)
        {
            return Conflict(new { message = "That caregiver is already assigned to this resident." });
        }

        if (outcome == ConfigurationOutcome.WouldLeaveNoCaregivers)
        {
            return BadRequest(new
            {
                message = "A resident must keep at least one caregiver. Assign another before removing this one."
            });
        }

        if (outcome == ConfigurationOutcome.LimitAboveMax)
        {
            return BadRequest(new
            {
                message = $"The escalation limit cannot exceed {ResidentConfigurationService.MaxEscalationLimit}."
            });
        }

        if (outcome == ConfigurationOutcome.EmergencyContactPhoneNumberTooLong)
        {
            return BadRequest(new
            {
                message =
                    $"The emergency contact number cannot be longer than " +
                    $"{ResidentConfigurationService.MaxEmergencyContactPhoneNumberLength} characters."
            });
        }

        if (outcome == ConfigurationOutcome.InvalidOrder)
        {
            return BadRequest(new
            {
                message = "The new order must include exactly the caregivers already assigned, each exactly once."
            });
        }

        return BadRequest(new { message = "The escalation limit must be at least 1." });
    }
}
