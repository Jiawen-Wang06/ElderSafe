namespace ElderSafe.Api.Detection;

/// <summary>Which fall detector the API runs.</summary>
public enum FallDetectorKind
{
    /// <summary>The rule-based <see cref="ThresholdFallDetector"/> (ADR-002).</summary>
    Threshold,

    /// <summary>The trained model, <see cref="OnnxFallDetector"/>.</summary>
    Onnx
}

/// <summary>
/// Where the trained fall model lives and how strictly a window is checked
/// before it is scored.
/// </summary>
public sealed class FallModelOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "FallModel";

    /// <summary>The ONNX file, relative to the content root.</summary>
    public string ModelPath { get; set; } = "Models/fall/model.onnx";

    /// <summary>The metadata written by the training run, relative to the content root.</summary>
    public string MetadataPath { get; set; } = "Models/fall/model.json";

    /// <summary>
    /// Shortest span of readings the model will score, in seconds.
    /// </summary>
    /// <remarks>
    /// The model was trained on 4 s windows, but the window is cut from the
    /// server's clock while readings carry the device's timestamps, so the
    /// newest reading always arrives a little late and the window covers
    /// slightly less than 4 s. Requiring the full 4 s would turn ordinary
    /// latency into missed falls. Measured on UMAFall, scoring windows 1 s
    /// short lowers sensitivity from 85.5% to 82.8% with no change in false
    /// alarms, so 3 s is the floor.
    /// </remarks>
    public double MinimumWindowSeconds { get; set; } = 3.0;

    /// <summary>
    /// Fraction of the expected readings, over the span actually covered, a
    /// window must contain to be scored. Below this, the window has gaps the
    /// model never saw in training.
    /// </summary>
    public double MinimumCoverage { get; set; } = 0.8;
}

/// <summary>
/// The metadata <c>tools/ml/train.py</c> writes next to the model.
/// </summary>
/// <remarks>
/// Only the fields the API needs are read. The file also carries the training
/// metrics, which belong to the model card rather than the running system.
/// </remarks>
public sealed record FallModelMetadata(
    string Name,
    double Threshold,
    IReadOnlyList<string> FeatureNames,
    double SampleRateHz,
    double WindowSeconds,
    string InputName,
    string OutputName,
    string Sha256);
