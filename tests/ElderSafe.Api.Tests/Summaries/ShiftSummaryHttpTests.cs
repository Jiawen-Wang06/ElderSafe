using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ElderSafe.Api.Alerting;
using ElderSafe.Api.Data;
using ElderSafe.Api.Data.Entities;
using ElderSafe.Api.Summaries;
using ElderSafe.Api.Tests.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElderSafe.Api.Tests.Summaries;

public class ShiftSummaryHttpTests
{
    private const string FirstCaregiver = "demo.caregiver@example.invalid";

    private static readonly Guid ResidentA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ResidentB = new("22222222-2222-2222-2222-222222222222");

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static WebApplicationFactory<Program> WithGenerator(ElderSafeWebAppFactory factory, ISummaryGenerator generator) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISummaryGenerator>();
            services.AddSingleton(generator);
        }));

    private static async Task<HttpClient> SignedInAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var response = await client.PostAsJsonAsync(
            "/api/auth/sign-in",
            new { UserName = FirstCaregiver, Password = ElderSafeWebAppFactory.DemoPassword });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        return client;
    }

    private static async Task<(Guid Answered, Guid Escalated)> AddAlertsAsync(WebApplicationFactory<Program> factory, Guid residentId)
    {
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ElderSafeDbContext>();
        var now = DateTimeOffset.UtcNow;

        var answered = new Alert
        {
            Id = Guid.NewGuid(),
            ResidentId = residentId,
            Kind = AlertKind.Fall,
            RaisedAt = now.AddHours(-3),
            LastNotifiedAt = now.AddHours(-3),
            AcknowledgedAt = now.AddHours(-3).AddMinutes(1),
            EscalationLevel = 0
        };

        var escalated = new Alert
        {
            Id = Guid.NewGuid(),
            ResidentId = residentId,
            Kind = AlertKind.Fall,
            RaisedAt = now.AddHours(-1),
            LastNotifiedAt = now.AddHours(-1).AddMinutes(2),
            AcknowledgedAt = now.AddHours(-1).AddMinutes(4),
            EscalationLevel = 1
        };

        context.Alerts.AddRange(answered, escalated);
        await context.SaveChangesAsync();

        return (answered.Id, escalated.Id);
    }

    private static async Task<ShiftSummaryView> SummarizeAsync(HttpClient client, Guid residentId)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/residents/{residentId}/shift-summary", new { TimeZone = "America/Toronto" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<ShiftSummaryView>(Json))!;
    }

    [Fact(DisplayName = "REQ-AI-01 with no model configured, the summary is written from the template and lists the shift's alerts")]
    [Trait("Requirement", "REQ-AI-01")]
    public async Task With_no_model_configured_the_summary_is_written_from_the_template()
    {
        using var factory = new ElderSafeWebAppFactory();
        var (answered, escalated) = await AddAlertsAsync(factory, ResidentA);
        var client = await SignedInAsync(factory);

        var summary = await SummarizeAsync(client, ResidentA);

        Assert.Equal(ShiftSummaryStatus.Generated, summary.Status);
        Assert.Equal(TemplateSummaryGenerator.Name, summary.Generator);
        Assert.Contains("[A1]", summary.Text);
        Assert.Contains("[A2]", summary.Text);
        Assert.Equal([answered, escalated], summary.Alerts.Select(a => a.Id));
        Assert.Equal(["A1", "A2"], summary.Alerts.Select(a => a.Ref));
        Assert.True(summary.Alerts[1].Escalated);
    }

    [Fact(DisplayName = "REQ-AI-02 a generated summary that fails validation is withheld, and the alerts are shown instead")]
    [Trait("Requirement", "REQ-AI-02")]
    public async Task A_generated_summary_that_fails_validation_is_withheld()
    {
        using var root = new ElderSafeWebAppFactory();
        var factory = WithGenerator(root, new FixedGenerator("A calm night. [A1] was answered, and [A9] was a false alarm."));
        await AddAlertsAsync(factory, ResidentA);
        var client = await SignedInAsync(factory);

        var summary = await SummarizeAsync(client, ResidentA);

        Assert.Equal(ShiftSummaryStatus.Rejected, summary.Status);
        Assert.Null(summary.Text);
        Assert.Contains(summary.Problems, p => p.Contains("A9"));
        Assert.Contains(summary.Problems, p => p.Contains("A2"));
        Assert.Equal(2, summary.Alerts.Count);
    }

    [Fact(DisplayName = "REQ-AI-02 a generated summary that passes validation is shown with the generator that wrote it")]
    [Trait("Requirement", "REQ-AI-02")]
    public async Task A_generated_summary_that_passes_validation_is_shown()
    {
        using var root = new ElderSafeWebAppFactory();
        var factory = WithGenerator(root, new FixedGenerator("Two alerts. [A1] was answered at once; [A2] escalated before it was acknowledged."));
        await AddAlertsAsync(factory, ResidentA);
        var client = await SignedInAsync(factory);

        var summary = await SummarizeAsync(client, ResidentA);

        Assert.Equal(ShiftSummaryStatus.Generated, summary.Status);
        Assert.Equal("Two alerts. [A1] was answered at once; [A2] escalated before it was acknowledged.", summary.Text);
        Assert.Equal(FixedGenerator.Name, summary.Generator);
        Assert.Empty(summary.Problems);
    }

    [Fact(DisplayName = "REQ-AI-02 when the model is unavailable, the template writes the summary and the caregiver is told why")]
    [Trait("Requirement", "REQ-AI-02")]
    public async Task When_the_model_is_unavailable_the_template_writes_the_summary()
    {
        using var root = new ElderSafeWebAppFactory();
        var factory = WithGenerator(root, new UnavailableGenerator("it is rate limited"));
        await AddAlertsAsync(factory, ResidentA);
        var client = await SignedInAsync(factory);

        var summary = await SummarizeAsync(client, ResidentA);

        Assert.Equal(ShiftSummaryStatus.Generated, summary.Status);
        Assert.Equal(TemplateSummaryGenerator.Name, summary.Generator);
        Assert.Contains("rate limited", summary.Notice);
        Assert.Contains("[A2]", summary.Text);
    }

    [Fact(DisplayName = "REQ-AI-01 nothing identifying the resident or a caregiver is sent to the model")]
    [Trait("Requirement", "REQ-AI-01")]
    public async Task Nothing_identifying_the_resident_or_a_caregiver_is_sent_to_the_model()
    {
        var recorder = new RecordingGenerator();
        using var root = new ElderSafeWebAppFactory();
        var factory = WithGenerator(root, recorder);
        var (answered, escalated) = await AddAlertsAsync(factory, ResidentA);
        var client = await SignedInAsync(factory);

        await SummarizeAsync(client, ResidentA);

        var prompt = AzureOpenAiSummaryGenerator.UserMessage(recorder.Input!);

        foreach (var secret in new[] { "Resident A", "Demo Caregiver", "Demo Nurse", ResidentA.ToString(), answered.ToString(), escalated.ToString() })
        {
            Assert.DoesNotContain(secret, prompt, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact(DisplayName = "REQ-SEC-01 a caregiver cannot request a shift summary for an unassigned resident")]
    [Trait("Requirement", "REQ-SEC-01")]
    [Trait("Requirement", "REQ-AI-01")]
    public async Task A_caregiver_cannot_request_a_shift_summary_for_an_unassigned_resident()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = await SignedInAsync(factory);

        var response = await client.PostAsJsonAsync($"/api/residents/{ResidentB}/shift-summary", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-03 an unauthenticated caller cannot request a shift summary")]
    [Trait("Requirement", "REQ-SEC-03")]
    [Trait("Requirement", "REQ-AI-01")]
    public async Task An_unauthenticated_caller_cannot_request_a_shift_summary()
    {
        using var factory = new ElderSafeWebAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync($"/api/residents/{ResidentA}/shift-summary", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class FixedGenerator(string text) : ISummaryGenerator
    {
        public const string Name = "Fixed test generator";

        public Task<SummaryDraft> GenerateAsync(ShiftSummaryInput input, CancellationToken cancellationToken) =>
            Task.FromResult(new SummaryDraft(text, Name));
    }

    private sealed class UnavailableGenerator(string reason) : ISummaryGenerator
    {
        public Task<SummaryDraft> GenerateAsync(ShiftSummaryInput input, CancellationToken cancellationToken) =>
            throw new SummaryGeneratorUnavailableException(reason);
    }

    private sealed class RecordingGenerator : ISummaryGenerator
    {
        public ShiftSummaryInput? Input { get; private set; }

        public Task<SummaryDraft> GenerateAsync(ShiftSummaryInput input, CancellationToken cancellationToken)
        {
            Input = input;

            return new TemplateSummaryGenerator().GenerateAsync(input, cancellationToken);
        }
    }
}
