namespace ElderSafe.Api.Summaries;

/// <summary>
/// Settings for the shift summary (REQ-AI-01, REQ-AI-02).
/// </summary>
public sealed class ShiftSummaryOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ShiftSummary";

    /// <summary>How far back a summary looks from the moment it is requested.</summary>
    public TimeSpan ShiftLength { get; set; } = TimeSpan.FromHours(12);

    /// <summary>
    /// The Azure OpenAI deployment that writes summaries. When any of its
    /// endpoint, deployment or key is missing, the template writes them
    /// instead, so the feature works with no cloud account at all.
    /// </summary>
    public AzureOpenAiOptions AzureOpenAI { get; set; } = new();
}

/// <summary>
/// Where the language model lives, and how long to wait for it.
/// </summary>
/// <remarks>
/// The key is never in <c>appsettings.json</c>. Locally it comes from
/// <c>dotnet user-secrets</c>, and in Azure from an App Service setting. The
/// endpoint and deployment name are not secrets, but this repository is
/// public, so they are kept beside the key rather than committed.
/// </remarks>
public sealed class AzureOpenAiOptions
{
    /// <summary>For example <c>https://my-resource.cognitiveservices.azure.com/</c>.</summary>
    public string? Endpoint { get; set; }

    /// <summary>The deployment name chosen in Foundry, not the model name.</summary>
    public string? Deployment { get; set; }

    /// <summary>The Azure OpenAI REST API version.</summary>
    public string ApiVersion { get; set; } = "2024-12-01-preview";

    /// <summary>Secret. Set through user secrets or the environment, never in a committed file.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// How long to wait before falling back to the template. A caregiver is
    /// waiting at a screen, and a late summary is worth less than a prompt
    /// one written from the template.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>True when every value needed to call the model is present.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(Deployment)
        && !string.IsNullOrWhiteSpace(ApiKey);
}
