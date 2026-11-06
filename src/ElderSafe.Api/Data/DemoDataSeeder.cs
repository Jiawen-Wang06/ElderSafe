using ElderSafe.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace ElderSafe.Api.Data;

/// <summary>
/// Creates demo residents and caregivers so the application can be explored.
/// </summary>
/// <remarks>
/// Every record here is invented. Keeping the demo data synthetic is what
/// makes it acceptable to publish sign-in credentials in a public README
/// (ADR-006), so nothing resembling a real person belongs in this file.
/// </remarks>
public sealed class DemoDataSeeder
{
    private static readonly Guid MargaretId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AlbertId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid FirstCaregiverId = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SecondCaregiverId = new("44444444-4444-4444-4444-444444444444");

    private readonly ElderSafeDbContext _context;
    private readonly DemoDataOptions _options;

    public DemoDataSeeder(ElderSafeDbContext context, DemoDataOptions options)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Seeds demo data if it is enabled and not already present.
    /// </summary>
    /// <returns>True if anything was written.</returns>
    public async Task<bool> SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            return false;
        }

        if (await _context.Residents.AnyAsync(cancellationToken))
        {
            return false;
        }

        var caregivers = new[]
        {
            new Caregiver
            {
                Id = FirstCaregiverId,
                UserName = "demo.caregiver@example.invalid",
                Email = "demo.caregiver@example.invalid",
                DisplayName = "Demo Caregiver"
            },
            new Caregiver
            {
                Id = SecondCaregiverId,
                UserName = "demo.nurse@example.invalid",
                Email = "demo.nurse@example.invalid",
                DisplayName = "Demo Nurse"
            }
        };

        var residents = new[]
        {
            new Resident
            {
                Id = MargaretId,
                DisplayName = "Resident A",
                SafeZoneLatitude = 43.4516,
                SafeZoneLongitude = -80.4925,
                SafeZoneRadiusMeters = 100,
                EmergencyCallEnabled = false
            },
            new Resident
            {
                Id = AlbertId,
                DisplayName = "Resident B",
                SafeZoneLatitude = 43.4643,
                SafeZoneLongitude = -80.5204,
                SafeZoneRadiusMeters = 150,
                EscalationLimit = 1,
                EmergencyCallEnabled = true
            }
        };

        var assignments = new[]
        {
            new ResidentCaregiver { ResidentId = MargaretId, CaregiverId = FirstCaregiverId, Order = 0 },
            new ResidentCaregiver { ResidentId = MargaretId, CaregiverId = SecondCaregiverId, Order = 1 },
            new ResidentCaregiver { ResidentId = AlbertId, CaregiverId = SecondCaregiverId, Order = 0 }
        };

        _context.Users.AddRange(caregivers);
        _context.Residents.AddRange(residents);
        _context.ResidentCaregivers.AddRange(assignments);

        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
