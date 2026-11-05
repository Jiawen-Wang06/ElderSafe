# ADR-005: Build the shift summary as grounded generation with a validator

**Status:** Accepted
**Date:** 2026-11-05

## Context

REQ-AI-01 requires a shift summary that mentions only events stored in the system and cites the identifier of each one. RISK-13 is the hazard behind it: a summary that invents an event, or omits one that happened, gives the incoming shift a wrong picture of the resident's night.

This feature was initially deferred on the grounds that it needed an API key and an evaluation suite. That reasoning was wrong, and reconsidering it changed the decision.

The expensive and uncertain part of an LLM feature is not the call. It is everything around the call: deciding what goes into the prompt, deciding what to do with output that cannot be trusted, and proving the result is safe. Those parts are ordinary software, and they can be written and tested without any model at all.

The valuable thing to demonstrate in a regulated domain is not that the author can call an API. It is that the author treats model output as untrusted input.

## Options

1. **Defer the feature** — saves a few hours, loses the most domain-relevant piece of AI work in the project.
2. **Call a model and show the result** — quick, but it demonstrates an API call and nothing about safety. It also cannot run in CI without a key and non-deterministic output.
3. **Ground, generate, validate** — put the model behind an interface, build the prompt only from stored events, and check the output against those events before it is shown.

## Decision

Implement the summary in three parts:

1. **Ground.** The prompt is built from stored events only, each carrying its ID, with an instruction to cite the ID of every event mentioned. Resident identifiers are included; names are not.
2. **Generate.** `ISummaryGenerator` has two implementations. `OpenAiSummaryGenerator` calls a model when a key is configured. `TemplateSummaryGenerator` produces a deterministic summary from the same events, needs no network, and is the default.
3. **Validate.** `SummaryValidator` checks the generated text against the input events. Every cited ID must exist in the input, no ID outside the input may be cited, and every escalated alert must appear. A summary that fails validation is rejected rather than shown.

The validator is a pure function over events and text.

## Consequences

**Good**

- The feature is complete and testable now. Turning on a real model later is configuration, not new code.
- The validator needs no API key, is deterministic, and runs in milliseconds, so REQ-AI-02 is verifiable in CI for free. An LLM feature that is genuinely gated in a pipeline is uncommon in portfolio projects.
- Treating model output as untrusted input is the behaviour the hazard analysis calls for, and it is what a reviewer in this domain will look for.
- The two implementations make the seam explicit: the system does not depend on a particular vendor.

**Bad**

- With no key configured, the shipped demo produces template text rather than model text. The README must say so rather than implying a model is running.
- The validator catches fabricated and missing *events*. It cannot catch a summary that cites real events and characterizes them misleadingly. That residual risk stays against RISK-13.
- A real model has not been evaluated against the scenarios, so no claim is made about its output quality.
- Prompt and model version become configuration that must be recorded with each release, like the detector's thresholds.

## Revisit when

A key is available and the evaluation scenarios can be run against a real model. At that point the eval results, the prompt and the model version are recorded with the release.
