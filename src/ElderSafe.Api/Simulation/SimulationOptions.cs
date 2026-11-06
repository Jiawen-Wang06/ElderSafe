namespace ElderSafe.Api.Simulation;

/// <summary>
/// Controls the device simulator.
/// </summary>
public sealed class SimulationOptions
{
    public const string SectionName = "Simulation";

    /// <summary>
    /// Whether scenarios can be triggered. Off by default.
    /// </summary>
    /// <remarks>
    /// The simulator is a sanctioned way to inject readings, which is RISK-15
    /// with permission. When this is off the endpoint does not answer at all,
    /// so a deployment holding real residents has no route to fabricated data.
    /// It must never be switched on in one.
    /// </remarks>
    public bool Enabled { get; set; }
}
