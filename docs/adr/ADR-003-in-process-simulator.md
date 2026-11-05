# ADR-003: Run the device simulator inside the API process

**Status:** Accepted
**Date:** 2026-11-05

## Context

There is no wearable. Something must produce accelerometer and GPS readings so the system can be demonstrated and tested. The original plan made the simulator a separate service posting to the API over HTTP, which mirrors how a real device behaves.

Within a two-day budget, a second process means a second project to build, a second thing to start, and a second failure mode when a reviewer tries to run the demo.

## Options

1. **Separate simulator service** — realistic, and it exercises the HTTP ingestion path the way a device would. Costs a project, a Dockerfile and startup orchestration.
2. **`BackgroundService` inside the API** — one process, one command to run, scenarios triggerable from the dashboard.
3. **Test fixtures only, no runtime simulator** — enough for unit tests, but there is nothing to show. A reviewer cannot watch an alert appear.

## Decision

Run the simulator as a `BackgroundService` in the API process, driven by named scenarios, with an endpoint to trigger one on demand.

The scenarios are the same fixtures the tests use: normal activity, a fall, GPS drift at the zone boundary, a wander out of the zone, and a device going silent.

The simulator posts through the same readings endpoint a real device would use, rather than calling the detectors directly. The ingestion path is therefore still exercised.

## Consequences

**Good**

- One command runs everything.
- A "simulate a fall" button on the dashboard lets a reviewer see the whole flow in about five seconds, which is the single most valuable thing the demo can offer.
- Sharing scenario fixtures between the simulator, the unit tests and the demo means all three stay consistent.

**Bad**

- Less realistic than a device on a network. Loss, latency, out-of-order and duplicate readings are not exercised by default, though scenarios can be written for them.
- Simulation code ships in the same assembly as production code. It is isolated behind a feature flag that is off unless explicitly enabled, and the flag's default is asserted in a test.
- Does not demonstrate device connectivity, which is one of the target role's nice-to-have areas.

## Revisit when

The project needs to demonstrate device connectivity, or to test network failure behaviour seriously.
