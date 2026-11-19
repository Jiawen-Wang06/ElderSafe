using ElderSafe.Api.Alerting;
using ElderSafe.Api.Data;
using ElderSafe.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace ElderSafe.Api.Summaries;

/// <summary>
/// Writes a handover summary of one resident's alerts over the last shift
/// (REQ-AI-01, REQ-AI-02), as ADR-005 designs it: ground, generate, validate.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><b>Ground.</b> The generator gets the stored alerts for the shift
/// and nothing else, each under a short reference it must cite.</item>
/// <item><b>Generate.</b> The configured generator writes the text. If it is
/// a remote model and it is unavailable, the template writes it instead and
/// the caregiver is told why.</item>
/// <item><b>Validate.</b> <see cref="SummaryValidator"/> checks the text
/// against the alerts. A summary that fails is withheld; the caregiver sees
/// the alerts and the reason instead.</item>
/// </list>
/// The stored alerts are returned with every summary, so the text can always
/// be checked against what it claims to describe.
/// </remarks>
public sealed class ShiftSummaryService
{
    private readonly ElderSafeDbContext _context;
    private readonly IResidentAccess _residentAccess;
    private readonly TimeProvider _timeProvider;
    private readonly ShiftSummaryOptions _options;
    private readonly EscalationOptions _escalationOptions;
    private readonly ISummaryGenerator _generator;
    private readonly ILogger<ShiftSummaryService> _logger;
    private readonly TemplateSummaryGenerator _template = new();

    public ShiftSummaryService(
        ElderSafeDbContext context,
        IResidentAccess residentAccess,
        TimeProvider timeProvider,
        ShiftSummaryOptions options,
        EscalationOptions escalationOptions,
        ISummaryGenerator generator,
        ILogger<ShiftSummaryService> logger)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _residentAccess = residentAccess ?? throw new ArgumentNullException(nameof(residentAccess));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _escalationOptions = escalationOptions ?? throw new ArgumentNullException(nameof(escalationOptions));
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Summarizes the shift ending now.
    /// </summary>
    /// <returns>
    /// Null if no such resident exists, or if the caregiver is not assigned
    /// to it. One answer for both, as on every caregiver-facing path (RISK-12).
    /// </returns>
    public async Task<ShiftSummaryView?> SummarizeAsync(
        Guid caregiverId,
        Guid residentId,
        string? timeZone,
        CancellationToken cancellationToken = default)
    {
        if (!await _context.Residents.AnyAsync(r => r.Id == residentId, cancellationToken))
        {
            return null;
        }

        if (!await _residentAccess.CanViewAsync(caregiverId, residentId, cancellationToken))
        {
            return null;
        }

        var to = _timeProvider.GetUtcNow();
        var from = to - _options.ShiftLength;

        // References follow time order, so A1 is the shift's first alert.
        var rows = (await _context.Alerts
            .Where(a => a.ResidentId == residentId && a.RaisedAt >= from && a.RaisedAt <= to)
            .Select(a => new
            {
                a.Id,
                a.Kind,
                a.RaisedAt,
                a.AcknowledgedAt,
                AcknowledgedBy = a.AcknowledgedByCaregiver == null ? null : a.AcknowledgedByCaregiver.DisplayName,
                a.EscalationLevel,
                a.DispatchedAt
            })
            .ToListAsync(cancellationToken))
            .OrderBy(a => a.RaisedAt)
            .Select((a, index) => (Ref: $"A{index + 1}", Row: a))
            .ToList();

        var events = rows
            .Select(r => new ShiftEvent(
                r.Ref, r.Row.Kind, r.Row.RaisedAt, r.Row.AcknowledgedAt, r.Row.EscalationLevel, r.Row.DispatchedAt))
            .ToList();

        var input = new ShiftSummaryInput(
            from, to, ResolveTimeZone(timeZone), _escalationOptions.ResponseTimeout, events);

        string? notice = null;
        SummaryDraft draft;

        try
        {
            draft = await _generator.GenerateAsync(input, cancellationToken);
        }
        catch (SummaryGeneratorUnavailableException exception)
        {
            _logger.LogWarning(exception, "Summary generator unavailable ({Reason}); using the template", exception.Reason);

            notice = $"The AI summary was unavailable because {exception.Reason}, so this one was written from a template.";
            draft = await _template.GenerateAsync(input, cancellationToken);
        }

        var problems = SummaryValidator.Validate(draft.Text, events);
        var status = problems.Count == 0 ? ShiftSummaryStatus.Generated : ShiftSummaryStatus.Rejected;

        // The outcome is logged, never the text: it describes a resident's
        // night, and logs are not where that belongs.
        _logger.LogInformation(
            "Shift summary for resident {ResidentId} by {Generator}: {Status} with {ProblemCount} problem(s) over {AlertCount} alert(s)",
            residentId, draft.Generator, status, problems.Count, events.Count);

        var alerts = rows
            .Select(r => new ShiftSummaryAlert(
                r.Ref,
                r.Row.Id,
                r.Row.Kind.ToString(),
                r.Row.RaisedAt,
                r.Row.AcknowledgedAt,
                r.Row.AcknowledgedBy,
                r.Row.EscalationLevel,
                r.Row.EscalationLevel > 0 || r.Row.DispatchedAt is not null,
                r.Row.DispatchedAt is not null))
            .ToList();

        return new ShiftSummaryView(
            residentId,
            from,
            to,
            status,
            status == ShiftSummaryStatus.Generated ? draft.Text : null,
            draft.Generator,
            notice,
            problems,
            alerts);
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZone) =>
        !string.IsNullOrWhiteSpace(timeZone) && TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone)
            ? zone
            : TimeZoneInfo.Utc;
}
