# ADR-005: Build the shift summary as grounded generation with a validator

**Status:** Accepted. Implemented 2026-11-18; see the implementation notes at the end.
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

## Implementation notes (2026-11-18)

Built as decided, with these details settled during implementation:

- **References, not raw IDs.** Each alert in the shift gets a short reference (`A1`, `A2`, …, in time order) and the model cites those. A model copies a short label far more reliably than a GUID, and the validator maps each one back to a stored alert. Stored IDs never reach the model.
- **What the model sees.** Alert times (in the caregiver's time zone), kinds, acknowledgement times, escalation counts and emergency-call times. No resident or caregiver names, and no stored identifiers (RISK-22). A test inspects the prompt for both.
- **Generators.**
  - `AzureOpenAiSummaryGenerator` calls an Azure OpenAI deployment (Microsoft Foundry, gpt-4.1-mini) over plain HTTPS rather than through an SDK, so no client library joins the SOUP list.
  - `TemplateSummaryGenerator` is the default whenever the endpoint, deployment or key is missing, so CI and a fresh clone never need a key.
- **Rejected versus unavailable.** These are handled differently on purpose:
  - A model answer that fails validation is **withheld**. The caregiver sees the reason and the stored alerts, as REQ-AI-02 requires, and the guardrail is visible in the demo rather than silently papered over.
  - A model that cannot answer is **replaced by the template**, whose output is validated the same way, and the caregiver is told why. This covers rate limiting (the deployment allows 15 requests a minute), a refused key, a timeout of 20 s, or the content filter.
- **On request only.** `POST /api/residents/{id}/shift-summary`, from a button. Nothing generates a summary automatically, so a dashboard left open costs nothing.
- **Recorded with each summary.** The model version Azure reports, and the prompt version (`shift-summary-v1` in `AzureOpenAiSummaryGenerator.PromptVersion`), are shown as the summary's author.
- **Configuration.** `ShiftSummary:AzureOpenAI` takes `Endpoint`, `Deployment` and `ApiKey`. In development they live in `dotnet user-secrets`, and in Azure in App Service settings. None are committed, because the repository is public. Tests blank the key, so a developer's real key is never spent by a test run.
- **Evaluation set.** Seven shifts covering every way an alert can end, from a quiet shift to an alert that reached an emergency call with no caregiver to escalate to. The template runs over all of them in CI (`SummaryEvaluationSet`).


## Live evaluation (2026-11-18)

The evaluation set's seven shifts were run through the API against the Azure OpenAI deployment (gpt-4.1-mini-2025-04-14), with times in America/Toronto. The emergency-call shift was run twice and the busy shift three times, making 10 summaries per prompt version.

| | Prompt v1 | Prompt v2 |
| --- | --- | --- |
| Passed validation | 10 of 10 | 10 of 10 |
| Invented or missing alerts | 0 | 0 |
| Said an unacknowledged alert was still open | 3 of 7 summaries with one | **7 of 7, each stated first** |
| Statements not in the data | 1: a quiet shift called "stable" | 0 |
| Advice the data does not support | Nearly all ("monitor closely", "please review") | 3 of 10 ("please address it as soon as possible") |
| Cited without brackets (`A1`, not `[A1]`) | 0 | 5 of 10 |

**What v1 showed.** Every v1 summary passed validation, and four of them were still misleading at handover. They left out that an alert was unacknowledged, which is the one fact an incoming caregiver most needs. This is the residual RISK-13 names: real alerts, described wrongly. The validator cannot see it, and only a person reading the output found it.

**What changed for v2.**
- The input gained an explicit `acknowledged` flag.
- The prompt now requires unacknowledged alerts first, each alert's acknowledgement stated, and no comments on the resident's condition or added advice.

**What remains.**
- Some advice still slips through.
- References sometimes lose their brackets. The validator already counted bare references, and the dashboard now links both forms.

These are acceptable for a handover note that is always shown above the stored alerts. They are not a reason to trust the text without that list.

**Re-running it.** The evaluation is manual because it needs the key and its output is not deterministic. Re-run it whenever the prompt, the model version or the input format changes, and bump `PromptVersion`.
