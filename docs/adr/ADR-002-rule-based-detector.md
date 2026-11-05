# ADR-002: Detect falls with thresholds rather than a trained model

**Status:** Accepted
**Date:** 2026-11-05

## Context

REQ-FALL-01 requires the system to detect a fall from motion data without any action by the resident. The original plan trained a model on a public fall dataset such as SisFall or UP-Fall and exported it to ONNX.

That work is realistically several days on its own: obtain and understand the dataset, check its licence, engineer features, split by subject rather than by recording, train, tune, evaluate, export, then wire the runtime into the API. The build budget for the whole project is one to two days.

There is also an honesty problem with the shortcut version. Training a model quickly and reporting "96% sensitivity" from a held-out split of simulated or lightly-handled data would produce a number that looks like evidence and is not. A reviewer who knows the domain would ask how the split was made, and the answer would be embarrassing.

## Options

1. **Train a model now** — closest to the real product, but blows the budget and risks a rushed evaluation that misleads.
2. **Threshold rules on accelerometer magnitude** — the classical approach in the fall-detection literature, implementable in an hour, and every decision it makes can be explained and unit-tested.
3. **Skip detection, trigger alerts by a button** — cheapest, but it removes the part of the product that matters and contradicts finding F-02, that a resident often cannot act after falling.

## Decision

Implement a rule-based detector behind an `IFallDetector` interface.

The rule recognizes the three-part shape of a fall inside a 3-second window, which matches finding F-01 that a fall takes under three seconds:

1. Free fall: acceleration magnitude drops below about 0.6 g
2. Impact: magnitude spikes above about 2.5 g within about a second of the drop
3. Stillness: low variance for about two seconds after impact

Thresholds live in configuration rather than in code.

A trained model implements the same interface when it is built. No other component changes.

## Consequences

**Good**

- Buildable in about an hour, with unit tests that can be read and argued with.
- Every decision is explainable, which matters for a Class C product where a reviewer may ask why a given event was or was not a fall.
- The stillness stage is what distinguishes a fall from sitting down heavily, and it is directly testable.
- The interface keeps the model path open without pretending it is done.

**Bad**

- **REQ-FALL-01's acceptance criteria cannot be met by this build.** The sensitivity and false-alarm targets require a held-out dataset that does not exist here. The requirement stays in the register with no passing test, and `traceability.md` shows the gap.
- Threshold rules generalize poorly. They will miss slow collapses and slides down a wall, and will fire on some non-falls.
- Any accuracy figure measured against the project's own simulator measures the simulator, not the detector. No such figure will be published as if it were an accuracy claim.

## Revisit when

A licensed dataset is in hand and there is time to split it by subject, train, and evaluate properly.
