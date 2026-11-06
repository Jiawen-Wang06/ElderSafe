# 02. Risk management file

| | |
| --- | --- |
| **Product** | ElderSafe |
| **Document status** | Draft v0.1 |
| **Date** | 2026-11-05 |
| **Safety class** | C (see [intended use](01-intended-use.md) section 7) |
| **Related documents** | [Intended use](01-intended-use.md), [User needs](03-user-needs.md), [Software requirements](04-software-requirements.md) |

> **Portfolio project. Not a medical device. Synthetic data only.**
> This file follows the structure of ISO 14971 to show the reasoning. It is not a regulatory submission. Probability estimates are the author's judgement and are not based on field data.

## 1. Scope and method

This file covers hazards arising from the ElderSafe software. It does not cover the wearable hardware, which is simulated in this project.

For each hazardous situation the file records the cause, the harm that could follow, a score before any risk control, the controls applied, and the residual score. Every control is implemented by a requirement and proven by a test.

Unlike the IEC 62304 safety class, which ignores probability, this analysis uses both severity and probability. That difference is deliberate: the class decides how much process the project applies, while this file decides which risks need controls and whether what remains is acceptable.

## 2. Scales

**Severity**

| Score | Label | Meaning |
| --- | --- | --- |
| 5 | Catastrophic | Death |
| 4 | Critical | Serious injury, or permanent impairment |
| 3 | Serious | Injury needing medical intervention, recoverable |
| 2 | Minor | Temporary discomfort or distress, no medical intervention |
| 1 | Negligible | Inconvenience only |

**Probability**

| Score | Label | Meaning |
| --- | --- | --- |
| 5 | Frequent | Expected often in normal use |
| 4 | Probable | Expected during the life of a deployment |
| 3 | Occasional | Could happen a few times |
| 2 | Remote | Unlikely but conceivable |
| 1 | Improbable | So unlikely it can be discounted |

**Acceptability.** Risk = severity × probability.

| Band | Score | Action |
| --- | --- | --- |
| Unacceptable | 15 and above, or any severity 5 with probability 3 or more | Must be reduced before release |
| Tolerable | 6 to 14 | Reduce as far as reasonably practicable, then justify |
| Acceptable | 5 and below | No further action required |

## 3. Hazard analysis

S = severity, P = probability, R = S × P.

### Detection hazards

| ID | Hazardous situation | Cause | Harm | S | P | R | Risk controls | S' | P' | R' |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| RISK-01 | A fall occurs and no alert is raised | Model false negative | Long lie: pressure injury, dehydration, rhabdomyolysis, hypothermia. Validated by finding F-02, a resident often cannot call for help | 4 | 4 | 16 | Sensitivity target of at least [95%] enforced in CI (REQ-FALL-01); detection requires no action by the resident (REQ-FALL-01); device heartbeat catches a silent device (REQ-SYS-01) | 4 | 2 | 8 |
| RISK-02 | Caregivers begin ignoring alerts | Too many false alarms | A real alert is dismissed, so help is delayed. Harm is the same as RISK-01 | 4 | 4 | 16 | False-alarm ceiling of at most [1] per resident per day enforced in CI (REQ-FALL-01); duplicate alerts for one incident are merged; the sensor data behind an alert is reviewable against the thresholds applied to it, so a suspected false alarm can be checked rather than only distrusted (REQ-FALL-08) | 4 | 2 | 8 |
| RISK-03 | A resident leaves the safe zone and no alert is raised | GPS inaccuracy, zone configured too wide, or readings missed | Resident outdoors and unfound. In a Canadian winter, exposure and death | 5 | 3 | 15 | Alert after [3] consecutive readings outside the zone (REQ-WANDER-01); device heartbeat (REQ-SYS-01); zone configurable by an authorized user (REQ-WANDER-07) | 5 | 2 | 10 |
| RISK-04 | A false wandering alert is raised | GPS drift near the zone boundary | Caregiver makes an unnecessary trip; repeated occurrences feed the alarm fatigue in RISK-02 | 2 | 4 | 8 | Consecutive-reading rule and an accuracy filter (REQ-WANDER-01) | 2 | 2 | 4 |
| RISK-05 | The wearable stops reporting and nobody notices | Battery, network loss, device removed | Falls and wandering go undetected for an extended period | 4 | 4 | 16 | A "no data" alert when a device is silent for [5 minutes] (REQ-SYS-01) | 4 | 2 | 8 |

### Alerting and escalation hazards

| ID | Hazardous situation | Cause | Harm | S | P | R | Risk controls | S' | P' | R' |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| RISK-06 | An alert is raised but nobody responds | Caregiver asleep, out of earshot, or unavailable. Validated by finding F-03 | Delayed help; harm as RISK-01 | 4 | 4 | 16 | Alerts require acknowledgement and escalate after [2 minutes] (REQ-FALL-03, REQ-WANDER-03); a caregiver can acknowledge from the dashboard (REQ-FALL-07); delivery does not depend on anyone being within earshot (REQ-FALL-02, REQ-WANDER-02) | 4 | 2 | 8 |
| RISK-07 | Escalation stops before reaching anyone available | Escalation configured to too few caregivers | The alert dies with one unavailable caregiver; harm as RISK-01 | 4 | 3 | 12 | Default is every assigned caregiver, and the minimum is 1 (REQ-FALL-04, REQ-WANDER-04); configuration changes are recorded in the audit log | 4 | 2 | 8 |
| RISK-08 | An alert is lost in delivery | Network failure, browser closed, service outage | Delayed help; harm as RISK-01 | 4 | 3 | 12 | Alerts stay open until acknowledged and escalate on timeout (REQ-FALL-03, REQ-WANDER-03); health checks on the alerting path | 4 | 2 | 8 |
| RISK-16 | An alert is raised but no caregiver has an active session | Session expired, browser closed, phone asleep, caregiver not at a screen | Nobody sees the alert, so help is delayed. The harm is the same as RISK-01, and the cause is the same shape as finding F-03: the system depends on somebody being present | 4 | 4 | 16 | Escalation runs server-side and continues regardless of who is connected (REQ-FALL-03); open alerts are shown on the next sign-in, so a missed push is recoverable; an emergency call still follows an exhausted chain (REQ-FALL-06) | 4 | 3 | 12 |

### Emergency-call hazards

| ID | Hazardous situation | Cause | Harm | S | P | R | Risk controls | S' | P' | R' |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| RISK-09 | An emergency call is placed for an incident that never happened | False positive, combined with caregivers not responding in time | Emergency resources diverted from a real emergency; distress to the resident; loss of trust leading to the feature being switched off | 3 | 3 | 9 | Off by default, enabled per resident by an authorized user (REQ-FALL-05, REQ-WANDER-05); only after every configured caregiver has failed to respond (REQ-FALL-06, REQ-WANDER-06); false-alarm ceiling (REQ-FALL-01) | 3 | 2 | 6 |
| RISK-10 | Several emergency calls are placed for one incident | Retry loop, duplicate alerts, or escalation restarting | Repeated calls block the line and waste dispatcher time | 3 | 3 | 9 | Exactly one call per incident (REQ-FALL-06, REQ-WANDER-06), verified by test | 3 | 1 | 3 |
| RISK-11 | An emergency call is configured but not placed when needed | Dispatch interface failure, or a defect in the escalation chain | The last safety net fails; harm as RISK-01 | 4 | 3 | 12 | Integration test against a simulated dispatch service (REQ-FALL-06, REQ-WANDER-06); a failed call attempt is surfaced to caregivers rather than failing silently | 4 | 2 | 8 |

### Data and information hazards

| ID | Hazardous situation | Cause | Harm | S | P | R | Risk controls | S' | P' | R' |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| RISK-14 | A stranger reads resident data | No authentication on an endpoint, or a session that does not expire | Location and health events disclosed to someone with no care relationship. Privacy breach under PHIPA. A resident with dementia whose whereabouts are exposed is also at risk of harm from a bad actor | 4 | 3 | 12 | Authentication required on every endpoint returning resident data (REQ-SEC-03); sessions expire; access recorded in the audit log (REQ-SEC-02) | 4 | 1 | 4 |
| RISK-12 | A caregiver sees another resident's data | Missing or wrong authorization check | A caregiver responds to the wrong person, or goes to the wrong address, while the real resident waits. Privacy breach under PHIPA | 3 | 3 | 9 | Per-resident authorization on every endpoint (REQ-SEC-01); every view and acknowledgement recorded in the audit log (REQ-SEC-02) | 3 | 1 | 3 |
| RISK-17 | Configuration weakens the resident's safety net | A resident account reduces the escalation limit, switches off emergency calling, or removes caregivers. For a resident living with dementia, the person the product protects is the one weakening their own protection | 4 | 3 | 12 | *Design:* the last caregiver cannot be removed and the escalation limit cannot fall below one (REQ-CFG-01, REQ-CFG-03). *Protective:* every change is written to the audit log and announced to the remaining caregivers, and the dashboard shows when emergency calling is off (REQ-CFG-05). *Information:* the instructions say a caregiver sets up and operates the resident account | 4 | 2 | 8 |
| RISK-13 | A shift summary states an event that did not happen, or omits one that did | LLM hallucination or omission | The incoming shift acts on a wrong picture of the resident's night | 3 | 4 | 12 | Summaries are built only from stored events and cite their IDs (REQ-AI-01); an evaluation suite runs in CI on every change to the feature | 3 | 2 | 6 |
| RISK-15 | Readings are fabricated for a resident | The readings endpoint accepts data from any caller, or a device credential is leaked or shared | Invented falls produce alerts that are not real, which is RISK-02 by another route; invented ordinary movement can mask a real fall, which is RISK-01. Fabricated location also misdirects a caregiver away from a resident who needs help | 4 | 3 | 12 | Readings accepted only from an authenticated device (REQ-SEC-04); each device is paired to one resident and the resident is derived from the pairing, so a credential cannot be used for anyone else; keys stored hashed | 4 | 2 | 8 |
| RISK-18 | A resident's alert history, aggregated over a period, is read by someone with no care relationship | Missing or wrong authorization check on the summary endpoint | Same privacy breach under PHIPA as RISK-12, now covering a resident's pattern of alerts over time rather than one event. A reader could also mistake the count for a clinical fall diagnosis, which it is not | 3 | 2 | 6 | Per-resident authorization on the summary endpoint, same rule as every other caregiver-facing path (REQ-SEC-01); the summary reports alerts raised, not falls that happened, so it cannot be read as a diagnosis (REQ-FALL-09); the requested range is bounded, so one request cannot pull a resident's entire history | 3 | 1 | 3 |

## 4. Residual risk

After controls, no hazard sits in the unacceptable band. Seven hazards remain tolerable at 6 to 10, and they share one shape: the software can fail to raise or deliver an alert, and the resident is then no better off than with no system at all.

That is the honest limit of this design. ElderSafe reduces the time until someone comes. It cannot guarantee that anyone comes. Two things follow:

1. The intended use states that the product supports routine supervision and does not replace it ([intended use](01-intended-use.md) section 1). Residents and caregivers must not be told it is a guarantee.
2. The known limitations in the README and the intended-use document must stay accurate, and must say plainly that some falls will be missed.

**The largest open item is RISK-16.** Every delivery path in this version requires a caregiver to be
signed in and looking at a screen. The escalation chain limits the harm, because an unresponsive caregiver
and an expired session are indistinguishable to the server and both move the alert along. But if no
assigned caregiver has a session, nobody is told until one signs in or the chain reaches an emergency call.
Closing it needs a notification channel that does not depend on an application session, which is deferred
([ADR-007](adr/ADR-007-dashboard-only-notification.md)).

**Overall assessment.** Against the intended use, residual risk is judged acceptable for a supervision aid used alongside normal care, and would not be acceptable for a product presented as a replacement for supervision.

## 5. Risks introduced by risk controls

ISO 14971 asks whether a control creates a new hazard. Two here do:

- **Escalation** (RISK-06's control) creates RISK-10, repeated or cascading notifications, if the escalation chain restarts. Controlled by the one-call-per-incident rule.
- **High sensitivity** (RISK-01's control) pushes false alarms up, which is RISK-02. The two targets are therefore set together, and both are enforced in the same CI gate so that neither can be improved alone.

## 6. Keeping this file current

- The pull request template asks whether a change affects this file.
- A new hazard found during development is added here before the fix is merged.
- Anomaly reports record which RISK-ID they touch.
- Probability estimates are reviewed when real evaluation results replace the placeholder targets.

## 7. Revision history

| Version | Date | Change |
| --- | --- | --- |
| 0.1 | 2026-11-05 | First draft: 13 hazards, scales, residual risk assessment |
| 0.2 | 2026-11-05 | RISK-14 added (unauthenticated access), following [ADR-006](adr/ADR-006-identity-cookie-auth.md) |
| 0.3 | 2026-11-05 | RISK-15 added (fabricated readings), following the decision to pair devices to residents |
| 0.4 | 2026-11-05 | RISK-16 added (no caregiver has an active session), following [ADR-007](adr/ADR-007-dashboard-only-notification.md) |
| 0.5 | 2026-11-06 | RISK-17 added (configuration weakens the safety net), with controls at all three ISO 14971 tiers |
| 0.6 | 2026-11-06 | Controls recorded for RISK-02 (REQ-FALL-08, the alert's sensor data is reviewable) and RISK-06 (REQ-FALL-07, acknowledgement from the dashboard). Residual estimates deliberately unchanged: reviewability helps a caregiver judge one alert but does not reduce the false-alarm rate, and acknowledgement was already assumed by the RISK-06 controls — this records a control that existed in the requirements without being implemented, which is not the same as lowering the risk |
| 0.7 | 2026-11-06 | RISK-18 added (a resident's aggregated alert history read by someone with no care relationship), following the summary view (REQ-FALL-09) |
