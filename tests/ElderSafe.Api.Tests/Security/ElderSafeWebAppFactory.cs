using ElderSafe.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElderSafe.Api.Tests.Security;

public sealed class ElderSafeWebAppFactory : WebApplicationFactory<Program>
{
    public const string DemoPassword = "Demo-Passw0rd!";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ElderSafeWebAppFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting("DemoData:Enabled", "true");
        builder.UseSetting("DemoData:CaregiverPassword", DemoPassword);
        builder.UseSetting("Alerting:EscalationSweepInterval", "01:00:00");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ElderSafeDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<ElderSafeDbContext>();

            services.AddDbContext<ElderSafeDbContext>(options => options.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
