using ElderSafe.Api.Alerting;
using ElderSafe.Api.Data;
using ElderSafe.Api.Detection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddDbContext<ElderSafeDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ElderSafe")
        ?? "Data Source=eldersafe.db"));

// Thresholds live in configuration rather than in code (ADR-002), so they can
// be tuned without a rebuild.
builder.Services.Configure<FallDetectorOptions>(
    builder.Configuration.GetSection(FallDetectorOptions.SectionName));
builder.Services.Configure<EscalationOptions>(
    builder.Configuration.GetSection(EscalationOptions.SectionName));
builder.Services.Configure<DemoDataOptions>(
    builder.Configuration.GetSection(DemoDataOptions.SectionName));

// The detector and the policy take their options directly rather than
// IOptions<T>, so they stay plain classes that a test can construct without
// any dependency-injection ceremony.
builder.Services.AddSingleton<IFallDetector>(provider =>
    new ThresholdFallDetector(
        provider.GetRequiredService<IOptions<FallDetectorOptions>>().Value));

builder.Services.AddSingleton<IEscalationPolicy>(provider =>
    new EscalationPolicy(
        provider.GetRequiredService<IOptions<EscalationOptions>>().Value));

builder.Services.AddScoped<DemoDataSeeder>(provider =>
    new DemoDataSeeder(
        provider.GetRequiredService<ElderSafeDbContext>(),
        provider.GetRequiredService<IOptions<DemoDataOptions>>().Value));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ElderSafeDbContext>();
    await context.Database.MigrateAsync();

    var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
    await seeder.SeedAsync();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

/// <summary>
/// Needed so the integration tests can reference this assembly's entry point
/// through <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program
{
}
