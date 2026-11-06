# ADR-007: Deliver alerts to the dashboard only, and defer push notification

**Status:** Accepted
**Date:** 2026-11-05

## Context

REQ-SEC-03 ends a caregiver's session after eight hours. That raised a
question with no comfortable answer: what happens when a resident falls and
the caregiver's session has already expired?

Working through it showed the session length was not the real problem. A web
dashboard requires the caregiver to be at a screen, signed in, with the
browser open. A thirty-day session would not help at three in the morning with
the laptop closed. The product currently depends on somebody being present and
looking — which is the same shape as finding F-03, where a person *was*
present and still did not notice.

What a real product does is separate two things this design currently
conflates:

| | Notification | Data access |
| --- | --- | --- |
| Says | "Something needs your attention" | The resident, the event, the location |
| Channel | Push, SMS, voice call | The dashboard |
| Needs a session | No | Yes |
| Carries health information | No | Yes |

That split also has a privacy benefit: a notification with no resident details
can travel over a channel the operator does not control, while everything
identifying stays behind authentication.

## Options

1. **Lengthen or remove session expiry** — reduces how often it bites, fixes
   nothing, and weakens REQ-SEC-03 in exchange.
2. **Implement push notification** — the right answer. Needs Firebase or APNs
   for mobile, or Twilio for SMS and voice: an account, credentials, a device
   registration flow, and a delivery-failure story of its own.
3. **Dashboard only, with the gap recorded** — ship what the budget allows and
   be explicit about what is missing.

## Decision

Deliver alerts to the dashboard over SignalR only. Record the gap as RISK-16
and as REQ-NOTIFY-01, which stays unverified in the traceability matrix.

Session expiry stays at eight hours. Lengthening it would trade a real
security property for a partial mitigation of a different problem.

## Consequences

**Good**

- Honest. The traceability matrix shows a requirement with no passing test,
  which is what "we have not built this" should look like.
- The escalation chain limits the harm: an unresponsive caregiver and an
  expired session are indistinguishable to the server, and both move the alert
  to the next caregiver and eventually to an emergency call.
- Signing in shows open alerts, so a missed notification is recoverable rather
  than lost.
- No dependency on an external notification provider, keeping the demo free to
  run and simple to deploy.

**Bad**

- **If no assigned caregiver has an active session, nobody is told** until one
  signs in or the chain reaches an emergency call. This is the largest
  residual risk in the project.
- The intended use must not imply alerts reach a caregiver who is not at a
  screen. The README and the intended-use document say so.
- One session policy currently serves two different deployments. A nursing
  station is a shared always-on display where a long session is reasonable; a
  family member's phone is not. REQ-SEC-03 assumes one answer for both, and a
  real product would separate them.

## Revisit when

A notification provider is available. The first version would send a
content-free push to registered devices when an alert is raised or escalated,
with the dashboard remaining the only place resident details appear.
