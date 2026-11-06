using System.Security.Claims;
using System.Text.Encodings.Web;
using ElderSafe.Api.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ElderSafe.Api.Security;

/// <summary>Options for the device authentication scheme.</summary>
public sealed class DeviceAuthenticationOptions : AuthenticationSchemeOptions
{
}

/// <summary>
/// Authenticates a wearable by the key it presents, and attaches the resident
/// it is paired to (REQ-SEC-04).
/// </summary>
/// <remarks>
/// A separate scheme from caregiver sign-in because the two are different
/// kinds of caller. A wearable cannot sign in as a person, and giving it a
/// caregiver account would mean a device credential could read resident data
/// as well as post readings.
/// </remarks>
public sealed class DeviceAuthenticationHandler
    : AuthenticationHandler<DeviceAuthenticationOptions>
{
    public const string SchemeName = "Device";
    public const string HeaderName = "X-Device-Key";
    public const string ResidentIdClaimType = "resident_id";

    private readonly ElderSafeDbContext _context;

    public DeviceAuthenticationHandler(
        IOptionsMonitor<DeviceAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ElderSafeDbContext context)
        : base(options, logger, encoder)
    {
        _context = context;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var presented))
        {
            return AuthenticateResult.NoResult();
        }

        var key = presented.ToString();

        if (string.IsNullOrWhiteSpace(key))
        {
            return AuthenticateResult.Fail("No device key was presented.");
        }

        var hash = DeviceKey.Hash(key);

        var device = await _context.Devices
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.KeyHash == hash && d.RevokedAt == null);

        if (device is null)
        {
            // One message for an unknown key and a revoked one. Telling the
            // caller which would confirm that a key was once valid.
            return AuthenticateResult.Fail("The device key was not recognised.");
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, device.Id.ToString()),
            new Claim(ClaimTypes.Name, device.Name),
            new Claim(ResidentIdClaimType, device.ResidentId.ToString())
        };

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return AuthenticateResult.Success(ticket);
    }
}
