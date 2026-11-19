namespace ElderSafe.Api.Summaries;

/// <summary>
/// Writes a shift summary from stored alerts.
/// </summary>
/// <remarks>
/// Implementations are not trusted. Whatever they return is checked by
/// <see cref="SummaryValidator"/> before anyone sees it (REQ-AI-02).
/// </remarks>
public interface ISummaryGenerator
{
    /// <exception cref="SummaryGeneratorUnavailableException">
    /// The generator could not produce a summary this time, for example
    /// because a remote model was rate limited or did not answer in time.
    /// </exception>
    Task<SummaryDraft> GenerateAsync(ShiftSummaryInput input, CancellationToken cancellationToken);
}

/// <summary>
/// A generator could not write a summary. The caller falls back to the
/// template rather than showing an error.
/// </summary>
public sealed class SummaryGeneratorUnavailableException : Exception
{
    public SummaryGeneratorUnavailableException(string reason, Exception? inner = null)
        : base(reason, inner)
    {
        Reason = reason;
    }

    /// <summary>A short reason a caregiver can be shown, e.g. "rate limited".</summary>
    public string Reason { get; }
}
