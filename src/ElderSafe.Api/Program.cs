using ElderSafe.Api.Alerting;
using ElderSafe.Api.Configuration;
using ElderSafe.Api.Data;
using ElderSafe.Api.Data.Entities;
using ElderSafe.Api.Detection;
using ElderSafe.Api.Security;
using ElderSafe.Api.Simulation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddSignalR();

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
builder.Services.Configure<AlertServiceOptions>(
    builder.Configuration.GetSection(AlertServiceOptions.SectionName));
builder.Services.Configure<SimulationOptions>(
    builder.Configuration.GetSection(SimulationOptions.SectionName));

builder.Services.AddIdentity<UserAccount, IdentityRole<Guid>>(options =>
{
    options.Password.RequiredLength = 12;
    options.SignIn.RequireConfirmedAccount = false;
    options.User.RequireUniqueEmail = false;

    // Slows password guessing against a username an attacker already knows.
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
})
.AddEntityFrameworkStores<ElderSafeDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "ElderSafe.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;

    // Strict rather than Lax: the dashboard is served from the same origin as
    // the API, so nothing legitimate is cross-site, and a cookie that is never
    // sent cross-site cannot be used for request forgery. Antiforgery tokens
    // would be the belt to this braces and are not implemented yet.
    options.Cookie.SameSite = SameSiteMode.Strict;

    // Absolute rather than sliding, so a session genuinely ends after eight
    // hours rather than lasting as long as somebody keeps a tab open
    // (REQ-SEC-03).
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = false;

    // This is an API, not a web site: an unauthenticated call gets a status
    // code, not a redirect to a sign-in page that does not exist.
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;

        return Task.CompletedTask;
    };

    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;

        return Task.CompletedTask;
    };
});

// A second scheme beside caregiver sign-in: a wearable is a different kind of
// caller, and giving it a caregiver account would let a device credential read
// resident data as well as post readings.
builder.Services.AddAuthentication()
    .AddScheme<DeviceAuthenticationOptions, DeviceAuthenticationHandler>(
        DeviceAuthenticationHandler.SchemeName,
        _ => { });

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentCaregiver, HttpContextCurrentCaregiver>();
builder.Services.AddScoped<IResidentAccess, ResidentAccess>();
builder.Services.AddSingleton<IAlertNotifier, SignalRAlertNotifier>();
builder.Services.AddSingleton(provider =>
    provider.GetRequiredService<IOptions<SimulationOptions>>().Value);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IEmergencyDispatch, LoggingEmergencyDispatch>();

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

builder.Services.AddScoped<AlertService>(provider =>
    new AlertService(
        provider.GetRequiredService<ElderSafeDbContext>(),
        provider.GetRequiredService<IFallDetector>(),
        provider.GetRequiredService<IEscalationPolicy>(),
        provider.GetRequiredService<IEmergencyDispatch>(),
        provider.GetRequiredService<TimeProvider>(),
        provider.GetRequiredService<IOptions<AlertServiceOptions>>().Value,
        provider.GetRequiredService<IResidentAccess>(),
        provider.GetRequiredService<IAlertNotifier>(),
        provider.GetRequiredService<ILogger<AlertService>>()));

builder.Services.AddScoped<ResidentConfigurationService>(provider =>
    new ResidentConfigurationService(
        provider.GetRequiredService<ElderSafeDbContext>(),
        provider.GetRequiredService<IAlertNotifier>(),
        provider.GetRequiredService<TimeProvider>(),
        provider.GetRequiredService<ILogger<ResidentConfigurationService>>()));

builder.Services.AddScoped<ReadingIngestionService>(provider =>
    new ReadingIngestionService(
        provider.GetRequiredService<ElderSafeDbContext>(),
        provider.GetRequiredService<AlertService>()));

builder.Services.AddHostedService<EscalationWorker>(provider =>
    new EscalationWorker(
        provider.GetRequiredService<IServiceScopeFactory>(),
        provider.GetRequiredService<TimeProvider>(),
        provider.GetRequiredService<IOptions<AlertServiceOptions>>().Value,
        provider.GetRequiredService<ILogger<EscalationWorker>>()));

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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<AlertsHub>("/hubs/alerts");

app.Run();

/// <summary>
/// Needed so the integration tests can reference this assembly's entry point
/// through <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program
{
}
