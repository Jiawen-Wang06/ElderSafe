# ADR-006: Authenticate caregivers with ASP.NET Core Identity and cookies

**Status:** Accepted
**Date:** 2026-11-05

## Context

The dashboard shows alerts about named residents, including their location. That is personal health information under PHIPA. An earlier draft of the architecture stubbed authentication with a fake current-user service to save time, leaving REQ-SEC-01 and REQ-SEC-02 with no passing test.

That trade was wrong for this product. A health application with no authentication is a conspicuous omission, and the two requirements it leaves untested are exactly the ones a reviewer in this domain would check first. RISK-12, a caregiver seeing another resident's data, cannot be controlled at all without knowing who the caregiver is.

Authentication also turns out to be cheaper than it was priced at. ASP.NET Core Identity provides password hashing, lockout and the user store, so the work is configuration and a seam, not cryptography.

## Options

1. **Keep the stub** — fastest, leaves the gap.
2. **Microsoft Entra ID** — the right answer for a real deployment: no password storage, MFA, enterprise sign-on. Costs a tenant, an app registration, and a reviewer cannot sign in to the public demo without being provisioned.
3. **ASP.NET Core Identity, cookie-based** — self-contained, works locally and on Azure with no external setup, and a reviewer can sign in with seeded demo credentials.
4. **Identity with bearer tokens** — what `MapIdentityApi` issues by default. Requires storing the token in the browser and passing it to SignalR through `accessTokenFactory`.

## Decision

Use ASP.NET Core Identity with **cookie** authentication.

Two design points follow:

**The caregiver is the user.** `Caregiver` derives from `IdentityUser` rather than sitting in a separate table joined to one. There is no identity-to-domain mapping to keep in sync, and a caregiver's assignments hang directly off the authenticated principal.

**One `DbContext`.** `ElderSafeDbContext` derives from `IdentityDbContext<Caregiver>`, so Identity's tables and the domain tables share a context and a migration history. The Visual Studio template's "Individual Accounts" option generates a second context instead, which is why the project is created with authentication set to None and Identity added deliberately.

Cookies are chosen over bearer tokens because the dashboard is served from the same origin as the API, so the browser attaches the cookie to both HTTP calls and the SignalR WebSocket with no token plumbing.

Two demo caregivers are seeded with known credentials, documented in the README and clearly marked as demo-only. Seeding is disabled unless the demo flag is on.

## Consequences

**Good**

- REQ-SEC-01 and REQ-SEC-02 become testable. The authorization test that matters — a caregiver requesting an unassigned resident and being refused — is now a real test rather than a deferred one.
- RISK-12 gains an actual control instead of a planned one.
- The audit log can record a real actor, which is what makes it worth having.
- No external dependency, so `dotnet run` still starts everything and the demo still works for a stranger.

**Bad**

- Passwords are stored, hashed, in the application's own database. Entra ID would avoid that entirely, and a real deployment should use it.
- Demo credentials in a public README mean the live demo is open to anyone who reads it. Acceptable because the data is synthetic, and it must stay synthetic.
- Cookie auth needs CSRF protection on state-changing endpoints, which bearer tokens would sidestep.
- Adds roughly two to three hours to the build, taken from the resident settings screen.

## Revisit when

The project is deployed anywhere real, at which point Entra ID replaces the local user store and the seeded demo accounts are removed.
