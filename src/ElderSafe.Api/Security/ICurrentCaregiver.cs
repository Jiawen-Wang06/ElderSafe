using System.Security.Claims;

namespace ElderSafe.Api.Security;

/// <summary>
/// The caregiver making the current request.
/// </summary>
/// <remarks>
/// Exists so that exactly one place in the application decides who is acting.
/// Before this, the acting caregiver arrived in a request body, which meant a
/// caller could name anybody and the audit log recorded a claim rather than a
/// fact (REQ-SEC-02).
/// </remarks>
public interface ICurrentCaregiver
{
    /// <summary>The signed-in caregiver, or null if nobody is signed in.</summary>
    Guid? Id { get; }
}

/// <summary>
/// Reads the current caregiver from the signed-in principal.
/// </summary>
public sealed class HttpContextCurrentCaregiver : ICurrentCaregiver
{
    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentCaregiver(IHttpContextAccessor accessor)
    {
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
    }

    public Guid? Id
    {
        get
        {
            var value = _accessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (Guid.TryParse(value, out var id))
            {
                return id;
            }

            return null;
        }
    }
}
