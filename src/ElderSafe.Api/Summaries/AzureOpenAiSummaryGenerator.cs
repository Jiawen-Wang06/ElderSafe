using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using ElderSafe.Api.Alerting;

namespace ElderSafe.Api.Summaries;

/// <summary>
/// Writes a shift summary with a model deployed in Azure OpenAI (Microsoft
/// Foundry).
/// </summary>
/// <remarks>
/// <para>
/// Called over plain HTTPS rather than through the Azure SDK. The request is
/// one documented REST call, and owning it means no extra library on the
/// SOUP list and full control over timeouts and failure handling.
/// </para>
/// <para>
/// Every failure becomes a <see cref="SummaryGeneratorUnavailableException"/>
/// with a short reason, and the caller falls back to the template. A missing
/// summary is harmless; an error screen at handover is not useful.
/// </para>
/// <para>
/// The prompt carries only times, alert kinds and outcomes: no resident or
/// caregiver names, and no identifiers outside the summary's own
/// <c>A1</c>-style references.
/// </para>
/// </remarks>
public sealed class AzureOpenAiSummaryGenerator : ISummaryGenerator
{
    /// <summary>
    /// Recorded with every summary this generator writes. Change it whenever
    /// <see cref="SystemPrompt"/> changes, so a summary can be traced to the
    /// instructions that produced it.
    /// </summary>
    public const string PromptVersion = "shift-summary-v2";

    /// <summary>The instructions sent with every request.</summary>
    /// <remarks>
    /// Version 2 follows the first live evaluation (ADR-005). Version 1's
    /// summaries all passed validation, but left out that an alert was still
    /// unacknowledged, called a quiet shift "stable", and added advice such
    /// as "monitor closely". None of that is in the data, and the validator
    /// cannot see it, so the rules now say so explicitly.
    /// </remarks>
    public const string SystemPrompt = """
        You write a short handover note for the caregiver starting the next shift.

        Rules:
        - Use only the alerts in the JSON you are given. Do not add alerts, times, people, causes, injuries or outcomes.
        - Refer to each alert you mention by its reference in square brackets, exactly as given, for example [A1].
        - Start with every alert whose "acknowledged" is false, and say plainly that it has not been acknowledged.
        - Mention every alert whose "escalated" is true, and say whether an emergency call was placed for it.
        - For each alert you mention, say whether it was acknowledged, and when.
        - An alert means the system detected a possible fall. Say "alert", not "fall", and never state that the resident fell or was hurt.
        - Report only what the JSON says. Do not comment on the resident's condition, and do not add advice or instructions.
        - If there are no alerts, say only that no alerts were raised during the shift.
        - Times are already in the caregiver's local time. Use them as given.
        - Write plain prose, at most five sentences, with no headings or lists.
        """;

    private static readonly JsonSerializerOptions PromptJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _http;
    private readonly AzureOpenAiOptions _options;

    public AzureOpenAiSummaryGenerator(HttpClient http, AzureOpenAiOptions options)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? throw new ArgumentNullException(nameof(options));

        if (!options.IsConfigured)
        {
            throw new ArgumentException("The Azure OpenAI endpoint, deployment and key must all be set.", nameof(options));
        }
    }

    public async Task<SummaryDraft> GenerateAsync(ShiftSummaryInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var url = $"{_options.Endpoint!.TrimEnd('/')}/openai/deployments/{Uri.EscapeDataString(_options.Deployment!)}"
            + $"/chat/completions?api-version={Uri.EscapeDataString(_options.ApiVersion)}";

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new
            {
                messages = new object[]
                {
                    new { role = "system", content = SystemPrompt },
                    new { role = "user", content = UserMessage(input) }
                },
                temperature = 0.2,
                max_tokens = 400
            })
        };
        request.Headers.Add("api-key", _options.ApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);

        HttpResponseMessage response;

        try
        {
            response = await _http.SendAsync(request, timeout.Token);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SummaryGeneratorUnavailableException("it did not answer in time", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new SummaryGeneratorUnavailableException("it could not be reached", exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new SummaryGeneratorUnavailableException(response.StatusCode switch
                {
                    HttpStatusCode.TooManyRequests => "it is rate limited",
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "the API key was refused",
                    HttpStatusCode.NotFound => "the deployment was not found",
                    _ => $"it returned HTTP {(int)response.StatusCode}"
                });
            }

            JsonNode? body;

            try
            {
                body = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
            }
            catch (JsonException exception)
            {
                throw new SummaryGeneratorUnavailableException("its answer could not be read", exception);
            }

            var choice = body?["choices"]?[0];

            if (choice?["finish_reason"]?.GetValue<string>() == "content_filter")
            {
                throw new SummaryGeneratorUnavailableException("its content filter withheld the answer");
            }

            var text = choice?["message"]?["content"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new SummaryGeneratorUnavailableException("it returned no text");
            }

            // The model version Azure reports, e.g. gpt-4.1-mini-2025-04-14,
            // rather than the deployment name alone: a deployment can be
            // pointed at a newer model without anything here changing.
            var model = body?["model"]?.GetValue<string>() ?? _options.Deployment;

            return new SummaryDraft(text.Trim(), $"Azure OpenAI {model}, prompt {PromptVersion}");
        }
    }

    /// <summary>The alerts, as the JSON the model reads.</summary>
    public static string UserMessage(ShiftSummaryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var payload = new
        {
            shiftStart = input.DayAndClock(input.ShiftStart),
            shiftEnd = input.DayAndClock(input.ShiftEnd),
            responseWindowMinutes = (int)input.ResponseTimeout.TotalMinutes,
            alerts = input.Alerts.OrderBy(a => a.RaisedAt).Select(a => new
            {
                @ref = a.Ref,
                kind = a.Kind == AlertKind.Fall ? "possible fall" : a.Kind.ToString(),
                raisedAt = input.Clock(a.RaisedAt),
                acknowledged = a.AcknowledgedAt is not null,
                acknowledgedAt = a.AcknowledgedAt is { } acknowledgedAt ? input.Clock(acknowledgedAt) : null,
                escalated = a.Escalated,
                timesEscalated = a.EscalationLevel,
                emergencyCallAt = a.EmergencyCallAt is { } called ? input.Clock(called) : null
            })
        };

        return JsonSerializer.Serialize(payload, PromptJson);
    }
}
