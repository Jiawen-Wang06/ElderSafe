using ElderSafe.Api.Data;
using ElderSafe.Api.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

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

/// <summary>Who is signed in.</summary>
public sealed record CurrentCaregiverResponse(Guid Id, string DisplayName, string UserName);

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
    public async Task<ActionResult<CurrentCaregiverResponse>> Me()
    {
        var caregiver = await _userManager.GetUserAsync(User);

        if (caregiver is null)
        {
            return Unauthorized();
        }

        return Ok(new CurrentCaregiverResponse(
            caregiver.Id,
            caregiver.DisplayName,
            caregiver.UserName ?? string.Empty));
    }
}
