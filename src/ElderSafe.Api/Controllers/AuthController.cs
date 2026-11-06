using ElderSafe.Api.Data.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ElderSafe.Api.Controllers;

/// <summary>Credentials offered at sign-in.</summary>
public sealed record SignInRequest(string UserName, string Password);

/// <summary>Who is signed in.</summary>
public sealed record CurrentCaregiverResponse(Guid Id, string DisplayName, string UserName);

/// <summary>
/// Signing caregivers in and out.
/// </summary>
/// <remarks>
/// There is deliberately no registration endpoint. Signing in proves who
/// somebody is; it must not decide what they may see. Access to a resident is
/// granted by assignment, and a public demo with self-registration plus
/// self-claimed access would be RISK-14 in one step.
/// </remarks>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly SignInManager<Caregiver> _signInManager;
    private readonly UserManager<Caregiver> _userManager;

    public AuthController(SignInManager<Caregiver> signInManager, UserManager<Caregiver> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
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
