using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using ElderSafe.Api.Summaries;

namespace ElderSafe.Api.Tests.Summaries;

public class AzureOpenAiSummaryGeneratorTests
{
    private static readonly AzureOpenAiOptions Options = new()
    {
        Endpoint = "https://example-resource.cognitiveservices.azure.com/",
        Deployment = "Summary_Deployment",
        ApiVersion = "2024-12-01-preview",
        ApiKey = "test-key-not-real",
        Timeout = TimeSpan.FromSeconds(5)
    };

    private static ShiftSummaryInput Busy => SummaryEvaluationSet.Scenarios["a busy shift with every outcome"];

    private static string Completion(string text, string finishReason = "stop") =>
        new JsonObject
        {
            ["model"] = "gpt-4.1-mini-2025-04-14",
            ["choices"] = new JsonArray(new JsonObject
            {
                ["finish_reason"] = finishReason,
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = text }
            })
        }.ToJsonString();

    [Fact(DisplayName = "REQ-AI-01 calls the configured deployment with the key and the shift's alerts")]
    [Trait("Requirement", "REQ-AI-01")]
    public async Task Calls_the_configured_deployment_with_the_key_and_the_shifts_alerts()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, Completion("Five alerts. [A2] and [A3] escalated."));
        var generator = new AzureOpenAiSummaryGenerator(new HttpClient(handler), Options);

        var draft = await generator.GenerateAsync(Busy, CancellationToken.None);

        Assert.Equal(
            "https://example-resource.cognitiveservices.azure.com/openai/deployments/Summary_Deployment/chat/completions?api-version=2024-12-01-preview",
            handler.Request!.RequestUri!.ToString());
        Assert.Equal("test-key-not-real", handler.Request.Headers.GetValues("api-key").Single());
        var messages = JsonNode.Parse(handler.Body)!["messages"]!.AsArray();
        Assert.Equal(AzureOpenAiSummaryGenerator.SystemPrompt, messages[0]!["content"]!.GetValue<string>());
        Assert.Contains("\"ref\":\"A5\"", messages[1]!["content"]!.GetValue<string>());
        Assert.Equal("Five alerts. [A2] and [A3] escalated.", draft.Text);
        Assert.Contains("gpt-4.1-mini-2025-04-14", draft.Generator);
        Assert.Contains(AzureOpenAiSummaryGenerator.PromptVersion, draft.Generator);
    }

    [Fact(DisplayName = "REQ-AI-01 the model is given alert times and outcomes only, with no names or stored identifiers")]
    [Trait("Requirement", "REQ-AI-01")]
    public void The_model_is_given_alert_times_and_outcomes_only()
    {
        var message = JsonNode.Parse(AzureOpenAiSummaryGenerator.UserMessage(Busy))!;
        var fields = message["alerts"]!.AsArray()
            .SelectMany(alert => alert!.AsObject().Select(property => property.Key))
            .ToHashSet();

        Assert.Equal(
            new[] { "ref", "kind", "raisedAt", "acknowledged", "acknowledgedAt", "escalated", "timesEscalated", "emergencyCallAt" }.Order(),
            fields.Order());
        Assert.Equal(5, message["alerts"]!.AsArray().Count);
    }

    [Theory(DisplayName = "REQ-AI-02 an unavailable model is reported as unavailable, not as a summary")]
    [InlineData(HttpStatusCode.TooManyRequests, "rate limited")]
    [InlineData(HttpStatusCode.Unauthorized, "key was refused")]
    [InlineData(HttpStatusCode.NotFound, "deployment was not found")]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP 500")]
    [Trait("Requirement", "REQ-AI-02")]
    public async Task An_unavailable_model_is_reported_as_unavailable(HttpStatusCode status, string reason)
    {
        var generator = new AzureOpenAiSummaryGenerator(new HttpClient(new FakeHandler(status, "{}")), Options);

        var exception = await Assert.ThrowsAsync<SummaryGeneratorUnavailableException>(
            () => generator.GenerateAsync(Busy, CancellationToken.None));

        Assert.Contains(reason, exception.Reason);
    }

    [Fact(DisplayName = "REQ-AI-02 an answer withheld by the content filter is reported as unavailable")]
    [Trait("Requirement", "REQ-AI-02")]
    public async Task An_answer_withheld_by_the_content_filter_is_reported_as_unavailable()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, Completion(string.Empty, "content_filter"));
        var generator = new AzureOpenAiSummaryGenerator(new HttpClient(handler), Options);

        var exception = await Assert.ThrowsAsync<SummaryGeneratorUnavailableException>(
            () => generator.GenerateAsync(Busy, CancellationToken.None));

        Assert.Contains("content filter", exception.Reason);
    }

    [Fact(DisplayName = "REQ-AI-02 a model that does not answer in time is reported as unavailable")]
    [Trait("Requirement", "REQ-AI-02")]
    public async Task A_model_that_does_not_answer_in_time_is_reported_as_unavailable()
    {
        var slow = Options;
        var options = new AzureOpenAiOptions
        {
            Endpoint = slow.Endpoint,
            Deployment = slow.Deployment,
            ApiKey = slow.ApiKey,
            Timeout = TimeSpan.FromMilliseconds(50)
        };
        var generator = new AzureOpenAiSummaryGenerator(new HttpClient(new FakeHandler(delay: TimeSpan.FromSeconds(10))), options);

        var exception = await Assert.ThrowsAsync<SummaryGeneratorUnavailableException>(
            () => generator.GenerateAsync(Busy, CancellationToken.None));

        Assert.Contains("in time", exception.Reason);
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        private readonly TimeSpan _delay;

        public FakeHandler(HttpStatusCode status = HttpStatusCode.OK, string body = "{}", TimeSpan delay = default)
        {
            _status = status;
            _body = body;
            _delay = delay;
        }

        public HttpRequestMessage? Request { get; private set; }

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);

            if (_delay > TimeSpan.Zero)
            {
                await Task.Delay(_delay, cancellationToken);
            }

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }
}
