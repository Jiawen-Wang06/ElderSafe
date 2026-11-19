using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace ElderSafe.Api.Detection;

/// <summary>
/// Detects a fall with a model trained in Azure Machine Learning and exported
/// to ONNX.
/// </summary>
/// <remarks>
/// <para>
/// Inference runs in-process. <see cref="Evaluate"/> does no I/O, so the
/// <see cref="IFallDetector"/> contract still holds: no side effects, and the
/// verdict is reproducible from the window alone. It also means detecting a
/// fall does not depend on a network call to a hosted endpoint, which would
/// add a failure mode exactly where a missed alert is the worst outcome.
/// </para>
/// <para>
/// The model file's SHA-256 is checked against its metadata when it loads,
/// and the same hash is registered with the model in Azure ML. Every verdict
/// names the model version, and that reason is written to the audit log, so
/// an alert can be traced to the training run that produced the model.
/// </para>
/// <para>
/// Known limitation: the model was trained on falls simulated by young
/// volunteers. No accuracy is claimed for real falls of older people
/// (REQ-FALL-11 remains deferred).
/// </para>
/// </remarks>
public sealed class OnnxFallDetector : IFallDetector, IDisposable
{
    private static readonly JsonSerializerOptions MetadataJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly InferenceSession _session;
    private readonly FallModelMetadata _metadata;
    private readonly FallModelOptions _options;

    public OnnxFallDetector(byte[] model, FallModelMetadata metadata, FallModelOptions options)
    {
        ArgumentNullException.ThrowIfNull(model);
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        var actual = Convert.ToHexStringLower(SHA256.HashData(model));

        if (!string.Equals(actual, metadata.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The fall model's hash does not match its metadata. Refusing to load a model whose origin cannot be shown.");
        }

        // A model trained on different features would still produce a score,
        // just a meaningless one. Catch that at startup, not in production.
        if (!metadata.FeatureNames.SequenceEqual(FallFeatures.Names))
        {
            throw new InvalidOperationException(
                "The fall model expects different features from the ones FallFeatures computes.");
        }

        _session = new InferenceSession(model);
    }

    /// <summary>Model name plus the start of its hash, e.g. <c>eldersafe-fall@3f2a9c1b04de</c>.</summary>
    public string ModelVersion => $"{_metadata.Name}@{_metadata.Sha256[..12]}";

    /// <summary>
    /// Loads the model and its metadata from disk. Called once at startup.
    /// </summary>
    public static OnnxFallDetector Load(string contentRoot, FallModelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var model = File.ReadAllBytes(Path.Combine(contentRoot, options.ModelPath));
        var metadata = JsonSerializer.Deserialize<FallModelMetadata>(
            File.ReadAllText(Path.Combine(contentRoot, options.MetadataPath)), MetadataJson)
            ?? throw new InvalidOperationException("The fall model metadata is empty.");

        return new OnnxFallDetector(model, metadata, options);
    }

    public FallVerdict Evaluate(IReadOnlyList<AccelerationSample> window)
    {
        if (window is null || window.Count < 2)
        {
            return FallVerdict.NotEnoughData("fewer than two readings");
        }

        var samples = window.OrderBy(s => s.At).ToArray();
        var span = (samples[^1].At - samples[0].At).TotalSeconds;

        // The model was trained on full windows. Scoring a much shorter one
        // is guessing, and an alert raised on a guess is evidence nobody has.
        if (span < _options.MinimumWindowSeconds)
        {
            return FallVerdict.NotEnoughData("the readings do not yet cover enough of the model's window");
        }

        var expected = (span * _metadata.SampleRateHz) + 1;

        if (samples.Length < expected * _options.MinimumCoverage)
        {
            return FallVerdict.NotEnoughData("too many readings are missing from the window");
        }

        var score = Score(samples);

        if (score < _metadata.Threshold)
        {
            return FallVerdict.NoFall(
                $"model {ModelVersion} scored {score:0.0000}, below its threshold of {_metadata.Threshold:0.0000}");
        }

        // The impact is the strongest reading in the window.
        var impact = samples.MaxBy(s => s.MagnitudeG);

        return new FallVerdict(
            FallOutcome.Fall,
            $"model {ModelVersion} scored {score:0.0000}, at or above its threshold of {_metadata.Threshold:0.0000}",
            impact.At);
    }

    /// <summary>The model's fall probability for a window, without the checks in <see cref="Evaluate"/>.</summary>
    /// <param name="samples">At least two samples, sorted by timestamp.</param>
    public double Score(IReadOnlyList<AccelerationSample> samples)
    {
        var features = FallFeatures.Compute(samples);
        var tensor = new DenseTensor<float>(features, [1, features.Length]);

        using var results = _session.Run(
            [NamedOnnxValue.CreateFromTensor(_metadata.InputName, tensor)],
            [_metadata.OutputName]);

        // Output shape is [1, 2]: probabilities of "no fall" and "fall".
        return results[0].AsTensor<float>()[0, 1];
    }

    public void Dispose() => _session.Dispose();
}
