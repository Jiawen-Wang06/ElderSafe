using System.Net;
using System.Net.Http.Json;
using ElderSafe.Api.Data;
using ElderSafe.Api.Data.Entities;
using ElderSafe.Api.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ElderSafe.Api.Tests.Security;

public class DeviceAuthenticationTests : IClassFixture<ElderSafeWebAppFactory>
{
    private static readonly Guid ResidentA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ResidentB = new("22222222-2222-2222-2222-222222222222");

    private readonly ElderSafeWebAppFactory _factory;

    public DeviceAuthenticationTests(ElderSafeWebAppFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient(string? deviceKey)
    {
        var client = _factory.CreateClient();

        if (deviceKey is not null)
        {
            client.DefaultRequestHeaders.Add(DeviceAuthenticationHandler.HeaderName, deviceKey);
        }

        return client;
    }

    private static object[] OneReading()
    {
        return
        [
            new
            {
                At = DateTimeOffset.UtcNow,
                AccelerationX = 0.0,
                AccelerationY = 0.0,
                AccelerationZ = 1.0,
                Latitude = (double?)null,
                Longitude = (double?)null,
                AccuracyMeters = (double?)null
            }
        ];
    }

    [Fact(DisplayName = "REQ-SEC-04 refuses readings with no device key")]
    [Trait("Requirement", "REQ-SEC-04")]
    public async Task Refuses_readings_with_no_device_key()
    {
        var client = CreateClient(null);

        var response = await client.PostAsJsonAsync("/api/readings", OneReading());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-04 refuses readings with an unrecognised device key")]
    [Trait("Requirement", "REQ-SEC-04")]
    public async Task Refuses_readings_with_an_unrecognised_device_key()
    {
        var client = CreateClient("not-a-real-key");

        var response = await client.PostAsJsonAsync("/api/readings", OneReading());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-04 accepts readings from the seeded demo device")]
    [Trait("Requirement", "REQ-SEC-04")]
    public async Task Accepts_readings_from_the_seeded_demo_device()
    {
        var client = CreateClient(ElderSafeWebAppFactory.DemoDeviceKey);

        var response = await client.PostAsJsonAsync("/api/readings", OneReading());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-04 attributes readings to the resident the device is paired to")]
    [Trait("Requirement", "REQ-SEC-04")]
    public async Task Attributes_readings_to_the_resident_the_device_is_paired_to()
    {
        var client = CreateClient(ElderSafeWebAppFactory.DemoDeviceKey);

        var response = await client.PostAsJsonAsync("/api/readings", OneReading());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ElderSafeDbContext>();

        Assert.True(await context.Readings.AnyAsync(r => r.ResidentId == ResidentA));
        Assert.False(await context.Readings.AnyAsync(r => r.ResidentId == ResidentB));
    }

    [Fact(DisplayName = "REQ-SEC-04 refuses readings from a revoked device")]
    [Trait("Requirement", "REQ-SEC-04")]
    public async Task Refuses_readings_from_a_revoked_device()
    {
        var key = DeviceKey.Generate();

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ElderSafeDbContext>();

            context.Devices.Add(new Device
            {
                Id = Guid.NewGuid(),
                Name = "Retired band",
                ResidentId = ResidentA,
                KeyHash = DeviceKey.Hash(key),
                PairedAt = DateTimeOffset.UtcNow.AddDays(-30),
                RevokedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });

            await context.SaveChangesAsync();
        }

        var client = CreateClient(key);

        var response = await client.PostAsJsonAsync("/api/readings", OneReading());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-04 does not let a device key read resident alerts")]
    [Trait("Requirement", "REQ-SEC-04")]
    public async Task Does_not_let_a_device_key_read_resident_alerts()
    {
        var client = CreateClient(ElderSafeWebAppFactory.DemoDeviceKey);

        var response = await client.GetAsync($"/api/residents/{ResidentA}/alerts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact(DisplayName = "REQ-SEC-04 stores the key hashed rather than the key itself")]
    [Trait("Requirement", "REQ-SEC-04")]
    public async Task Stores_the_key_hashed_rather_than_the_key_itself()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ElderSafeDbContext>();

        var devices = await context.Devices.AsNoTracking().ToListAsync();

        Assert.NotEmpty(devices);
        Assert.All(devices, d => Assert.DoesNotContain(
            ElderSafeWebAppFactory.DemoDeviceKey, d.KeyHash, StringComparison.Ordinal));
    }
}
