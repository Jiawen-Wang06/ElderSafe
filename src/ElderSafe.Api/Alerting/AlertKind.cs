namespace ElderSafe.Api.Alerting;

/// <summary>
/// What an alert is about.
/// </summary>
public enum AlertKind
{
    /// <summary>A probable fall (REQ-FALL-01).</summary>
    Fall,

    /// <summary>A resident outside their safe zone (REQ-WANDER-01).</summary>
    Wander,

    /// <summary>A device that has stopped reporting (REQ-SYS-01).</summary>
    NoData
}
