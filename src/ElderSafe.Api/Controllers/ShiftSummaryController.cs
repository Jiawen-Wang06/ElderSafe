using ElderSafe.Api.Security;
using ElderSafe.Api.Summaries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ElderSafe.Api.Controllers;

/// <summary>
/// Writes a handover summary of a resident's last shift (REQ-AI-01, REQ-AI-02).
/// </summary>
/// <remarks>
/// POST rather than GET: each call may cost a model request and is subject
/// to its rate limit, so it should happen when a caregiver asks for it, never
/// because a browser or proxy decided to prefetch or retry a GET.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/residents/{residentId:guid}/shift-summary")]
public sealed class ShiftSummaryController : ControllerBase
{
    private readonly ShiftSummaryService _summaries;
    private readonly ICurrentCaregiver _currentCaregiver;

    public ShiftSummaryController(ShiftSummaryService summaries, ICurrentCaregiver currentCaregiver)
    {
        _summaries = summaries;
        _currentCaregiver = currentCaregiver;
    }

    [HttpPost]
    public async Task<ActionResult<ShiftSummaryView>> Summarize(
        Guid residentId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ShiftSummaryRequest? request,
        CancellationToken cancellationToken)
    {
        var caregiverId = _currentCaregiver.Id;

        if (caregiverId is null)
        {
            return Unauthorized();
        }

        var summary = await _summaries.SummarizeAsync(
            caregiverId.Value, residentId, request?.TimeZone, cancellationToken);

        // Not found, or not one of theirs: the same answer, so the API never
        // confirms that a resident exists to someone with no business knowing
        // (RISK-12).
        if (summary is null)
        {
            return NotFound();
        }

        return Ok(summary);
    }
}
