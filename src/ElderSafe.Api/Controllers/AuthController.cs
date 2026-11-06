using ElderSafe.Api.Data;
using ElderSafe.Api.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ElderSafe.Api.Controllers;

/// <summary>A new account.</summary>
/// <param name="UserName">Used to sign in, and to assign a caregiver.</param>
/// <param name="AccountType">Resident or Caregiver.</param>
public sealed record RegisterRequest(
    string UserName,
    string Password,
    string DisplayName,
    AccountType AccountType);

/// <summary>Credentials offered at sign-in.</summary>
public sealed record SignInRequest(string UserName, string Password);

/// <summary>A resident this account may see.</summary>
public sealed record ResidentSummary(Guid Id, string DisplayName);

/// <summary>Who is signed in, and what they can reach.</summary>
/// <remarks>
/// The residents are included so the dashboard never has to guess what an
/// account may open. A resident account sees the one it owns; a caregiver
/// sees the ones assigned to them. Anything absent from this list is
/// something the API would refuse anyway.
/// </remarks>
public sealed record CurrentAccountResponse(
    Guid Id,
    string DisplayName,
    string UserName,
    string AccountType,
    IReadOnlyList<ResidentSummary> Residents);

/// <summary>
/// Signing caregivers in and out.
/// </summary>
/// <remarks>
/// Registering proves who somebody is and grants access to nothing. A new
/// caregiver account sees no residents until one assigns them; a new resident
/// account sees only itself. That separation is what makes open registration
/// safe here — it is self-claimed *access*, not self-service identity, that
/// would be RISK-14.
/// </remarks>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly SignInManager<UserAccount> _signInManager;
    private readonly UserManager<UserAccount> _userManager;
    private readonly ElderSafeDbContext _context;

    public AuthController(
        SignInManager<UserAccount> signInManager,
        UserManager<UserAccount> userManager,
        ElderSafeDbContext context)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _context = context;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var account = new UserAccount
        {
            Id = Guid.NewGuid(),
            UserName = request.UserName,
            Email = request.UserName,
            DisplayName = request.DisplayName,
            AccountType = request.AccountType
        };

        var created = await _userManager.CreateAsync(account, request.Password);

        if (!created.Succeeded)
        {
            return BadRequest(new { errors = created.Errors.Select(e => e.Description) });
        }

        // A resident account owns one resident record from the moment it
        // exists, so there is never a resident account with nothing to
        // configure.
        if (request.AccountType == AccountType.Resident)
        {
            _context.Residents.Add(new Resident
            {
                Id = Guid.NewGuid(),
                DisplayName = request.DisplayName,
                AccountId = account.Id
            });

            await _context.SaveChangesAsync();
        }

        return NoContent();
    }

    [HttpPost("sign-in")]
    [AllowAnonymous]
    public async Task<IActionResult> SignIn([FromBody] SignInRequest request)
    {
        // lockoutOnFailure limits password guessing against a known username.
        var result = await _signInManager.PasswordSignInAsync(
            request.UserName,
            request.Password,
            isPersistent: false,
            lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            // One message for every failure. Distinguishing "no such user"
            // from "wrong password" tells an attacker which usernames exist.
            return Unauthorized(new { message = "Sign-in failed." });
        }

        return NoContent();
    }

    [HttpPost("sign-out")]
    [Authorize]
    public async Task<IActionResult> SignOutCaregiver()
    {
        await _signInManager.SignOutAsync();

        return NoContent();
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<CurrentAccountResponse>> Me()
    {
        var account = await _userManager.GetUserAsync(User);

        if (account is null)
        {
            return Unauthorized();
        }

        var residents = await _context.Residents
            .Where(r => r.AccountId == account.Id
                || r.Caregivers.Any(rc => rc.CaregiverId == account.Id))
            .OrderBy(r => r.DisplayName)
            .Select(r => new ResidentSummary(r.Id, r.DisplayName))
            .ToListAsync();

        return Ok(new CurrentAccountResponse(
            account.Id,
            account.DisplayName,
            account.UserName ?? string.Empty,
            account.AccountType.ToString(),
            residents));
    }
}
