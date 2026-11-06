# 04. Software requirements

| | |
| --- | --- |
| **Product** | ElderSafe |
| **Document status** | Draft v0.4. Numbers in [brackets] are starting targets to be tuned and then fixed. |
| **Date** | 2026-11-05 |
| **Related documents** | [Intended use](01-intended-use.md), [Risk management](02-risk-management.md), [User needs](03-user-needs.md) |

> **Portfolio project. Not a medical device. Synthetic data only.**

## 1. How to read this document

- Every requirement is one testable statement with an ID, written with "shall".
- "Comes from" points to a user need (UN-xx) and, where the requirement exists to control a hazard, a risk (RISK-xx).
- "Verified by" names how the requirement is proven. Each automated test carries its requirement ID as a tag, and CI builds `traceability.md` from those tags.
- Fall and wandering requirements are kept separate on purpose. They share escalation behaviour, which is implemented once, but each requirement is tested on both paths.

**Terms**

| Term | Meaning |
| --- | --- |
| Caregiver | A person assigned to a resident who receives and acknowledges alerts |
| Escalation order | The ordered list of caregivers assigned to a resident |
| Failed to respond | A caregiver did not acknowledge the alert within [2 minutes] |
| Authorized user | A resident's administrator or primary caregiver |
| Emergency call | A request to place a 911 call. **In this project it goes to a simulated dispatch service. No real call is ever placed.** |

## 2. Fall detection

| ID | Requirement | Acceptance criteria | Comes from | Verified by |
| --- | --- | --- | --- | --- |
| REQ-FALL-01 | The system shall detect a fall from the wearable's motion data without any action by the resident. | On the held-out test set, at least [95%] of falls are detected, with at most [1] false alarm per resident per day of simulated activity. No resident input is required at any point in detection or alerting. | UN-01, UN-03, UN-11; RISK-01, RISK-02 | Model evaluation in CI; automated test |
| REQ-FALL-02 | When a fall is detected, the system shall send an alert to the resident's first caregiver. | The alert appears on that caregiver's dashboard within [5 seconds] of detection. Delivery does not depend on anyone being within earshot of the resident. | UN-01, UN-12; RISK-06 | Integration and end-to-end test |
| REQ-FALL-03 | If a caregiver has failed to respond, the system shall escalate the alert to the next caregiver in the escalation order. | Escalation happens after [2 minutes] without acknowledgement. An acknowledged alert is never escalated. | UN-04; RISK-06, RISK-08 | Automated test with a fake clock |
| REQ-FALL-04 | An authorized user shall be able to configure how many caregivers an alert escalates to. | Value is between 1 and the number of assigned caregivers. Escalation stops after that many. Default is all assigned caregivers. The change is recorded in the audit log. | UN-04, UN-09; RISK-07 | Automated test |
| REQ-FALL-05 | An authorized user shall be able to configure whether an emergency call is made when all configured caregivers fail to respond. | Default is off. The setting is saved per resident. The change is recorded in the audit log. | UN-04, UN-09; RISK-09 | Automated test |
| REQ-FALL-06 | If the emergency-call setting is on and all configured caregivers have failed to respond, the system shall make an emergency call. | Exactly one call is requested per incident. It carries the resident's identifier and last known location. No call is requested if the setting is off or any caregiver acknowledged. A failed call attempt is surfaced to caregivers. | UN-04; RISK-09, RISK-10, RISK-11 | Integration test with a simulated dispatch service |
| REQ-FALL-07 | A caregiver shall be able to acknowledge an alert for a resident they are assigned to. | Acknowledgement records which caregiver acted and when, taken from the session and never from the request. An acknowledged alert is not escalated afterwards. Acknowledging an alert for a resident the caregiver is not assigned to is refused, indistinguishably from the alert not existing. A second acknowledgement does not overwrite the first. | UN-04; RISK-06, RISK-12 | Integration test |
| REQ-FALL-08 | A caregiver shall be able to review the sensor data that produced an alert, against the thresholds applied to it. | The trace covers the window the detector judged plus context either side. The thresholds reported are the ones in effect, not values fixed in the client. Access follows the same per-resident rule as every other caregiver-facing path. | UN-04; RISK-02, RISK-12 | Integration test |
| REQ-FALL-09 | A caregiver shall be able to see counts of alerts raised for a resident they are assigned to, over a date range they choose. | Counts are broken down by alert kind and by whether the alert was responded to within the configured response window or escalated. The range is bounded to [400 days] per request. The summary reports alerts raised, never a diagnosis of how many falls occurred, since the system has no way to know the second number. Access follows the same per-resident rule as every other caregiver-facing path. | UN-04; RISK-02, RISK-12, RISK-18 | Automated test; integration test |

## 3. Wandering detection

| ID | Requirement | Acceptance criteria | Comes from | Verified by |
| --- | --- | --- | --- | --- |
| REQ-WANDER-01 | The system shall detect when a resident leaves their safe zone. | An alert is raised after [3] consecutive location readings outside the zone. A single stray reading, or one with poor accuracy, does not raise an alert in the GPS-drift scenario. | UN-02, UN-03; RISK-03, RISK-04 | Automated test with simulator scenarios |
| REQ-WANDER-02 | When a resident is detected outside the safe zone, the system shall send an alert to the resident's first caregiver. | The alert appears on that caregiver's dashboard within [5 seconds] of detection. | UN-02, UN-12; RISK-06 | Integration and end-to-end test |
| REQ-WANDER-03 | If a caregiver has failed to respond, the system shall escalate the alert to the next caregiver in the escalation order. | Escalation happens after [2 minutes] without acknowledgement. An acknowledged alert is never escalated. | UN-04; RISK-06, RISK-08 | Automated test with a fake clock |
| REQ-WANDER-04 | An authorized user shall be able to configure how many caregivers an alert escalates to. | Value is between 1 and the number of assigned caregivers. Escalation stops after that many. Default is all assigned caregivers. The change is recorded in the audit log. | UN-04, UN-09; RISK-07 | Automated test |
| REQ-WANDER-05 | An authorized user shall be able to configure whether an emergency call is made when all configured caregivers fail to respond. | Default is off. The setting is saved per resident. The change is recorded in the audit log. | UN-04, UN-09; RISK-09 | Automated test |
| REQ-WANDER-06 | If the emergency-call setting is on and all configured caregivers have failed to respond, the system shall make an emergency call. | Exactly one call is requested per incident. It carries the resident's identifier and last known location. No call is requested if the setting is off or any caregiver acknowledged. A failed call attempt is surfaced to caregivers. | UN-04; RISK-09, RISK-10, RISK-11 | Integration test with a simulated dispatch service |
| REQ-WANDER-07 | An authorized user shall be able to configure the resident's safe zone. | The zone is a centre point and a radius. A saved zone applies to the next location reading. The change is recorded in the audit log. | UN-10; RISK-03 | Automated test |

## 4. Device and system health

| ID | Requirement | Acceptance criteria | Comes from | Verified by |
| --- | --- | --- | --- | --- |
| REQ-SYS-01 | The system shall raise a "no data" alert when a resident's device has not reported for [5 minutes]. | The alert reaches the first caregiver and follows the same escalation rules as other alerts. It clears automatically when reporting resumes. | UN-05; RISK-05, RISK-01, RISK-03 | Automated test with a fake clock |

## 5. Access and accountability

| ID | Requirement | Acceptance criteria | Comes from | Verified by |
| --- | --- | --- | --- | --- |
| REQ-SEC-03 | The system shall require a caregiver to authenticate before any resident data is returned. | Every endpoint returning resident data refuses an unauthenticated request. The dashboard shows a sign-in screen until authentication succeeds. Sessions expire after [8 hours]. | UN-06; RISK-14 | Automated test on an unauthenticated request to each endpoint |
| REQ-SEC-01 | The system shall show a caregiver only the residents assigned to them. | A request for an unassigned resident is refused on every endpoint, including direct API calls. | UN-06; RISK-12 | Automated test attempting cross-resident access |
| REQ-SEC-02 | The system shall record every view and acknowledgement of resident data in an append-only audit log. | Each entry holds who, what, and when. Existing entries cannot be changed or deleted through the application. | UN-07; RISK-12 | Automated test |
| REQ-SEC-04 | The system shall accept readings only from an authenticated device, and shall attribute them to the resident that device is paired to. | A request with no device credential, or an unrecognised one, is refused. The resident is taken from the pairing and never from the request, so a device credential cannot be used to post readings for another resident. Keys are stored hashed. | UN-06; RISK-15 | Automated test posting without a key, and with another device's key |

## 6. Shift summary (AI-assisted)

| ID | Requirement | Acceptance criteria | Comes from | Verified by |
| --- | --- | --- | --- | --- |
| REQ-AI-01 | A shift summary shall mention only events stored in the system, and shall cite the identifier of each event it mentions. | On the evaluation set, no summary states an event that is absent from the input, and no summary omits an alert that was escalated. Every cited identifier exists. | UN-08; RISK-13 | Evaluation suite in CI |
| REQ-AI-02 | The system shall validate a generated summary against the events it was given, and shall not show a summary that fails validation. | Validation rejects a summary citing an identifier absent from the input, and one omitting an escalated alert. A rejected summary is not displayed; the caregiver sees the underlying events instead. Validation runs on every summary, whichever generator produced it. | UN-08; RISK-13 | Unit tests on the validator, with no model call |

## 7. Resident configuration

| ID | Requirement | Acceptance criteria | Comes from | Verified by |
| --- | --- | --- | --- | --- |
| REQ-CFG-01 | A resident account shall be able to change its escalation limit and emergency-call setting, and no other account shall. | The resident's own account can change both. A caregiver assigned to that resident can read them and cannot change them. An escalation limit below 1 or above 10 is refused. | UN-09; RISK-17, RISK-07 | Automated test |
| REQ-CFG-02 | A resident account shall be able to assign a caregiver by the username they registered with. | A known caregiver account is assigned and appears in the escalation order. An unknown username is refused. Assigning the same caregiver twice is refused. | UN-04; RISK-12 | Automated test |
| REQ-CFG-03 | The system shall refuse any change that would leave a resident with no assigned caregivers. | Removing the last caregiver is refused and the assignment remains. | UN-04; RISK-07, RISK-17 | Automated test |
| REQ-CFG-04 | Registering an account shall grant access to no resident other than one the account owns. | A newly registered caregiver account can read no resident until assigned. A newly registered resident account owns exactly one resident and can read no other. | UN-06; RISK-14 | Automated test |
| REQ-CFG-05 | Every configuration change shall be recorded in the audit log and announced to the resident's assigned caregivers. | The audit entry names the acting account. Connected caregivers receive the change. | UN-07; RISK-17 | Automated test |
| REQ-CFG-06 | A resident account shall be able to set, change, or clear a phone number associated with the emergency-call setting. | The number is stored and returned on read. Blank or whitespace input clears it rather than storing empty text. A number longer than 32 characters is refused. The number is never passed to anything capable of placing a real call; the system's only dispatch implementation remains a logging-only simulation. | UN-09; RISK-09 | Automated test |
| REQ-CFG-07 | A resident account shall be able to look up a caregiver by their exact registered email before assigning them, seeing the caregiver's display name on a match. | An exact match returns the display name. No match returns a plain not-found result. A string that is a prefix or substring of a registered email, but not the whole thing, does not match. Only a resident configuring that specific resident may use the lookup. | UN-04; RISK-19 | Automated test |

## 8. Notification delivery

| ID | Requirement | Acceptance criteria | Comes from | Verified by |
| --- | --- | --- | --- | --- |
| REQ-NOTIFY-01 | The system shall notify an assigned caregiver through a channel that does not require an open application session. | A caregiver with no active session is still told that an alert needs attention. The notification names no resident and carries no health information; details require signing in. | UN-04, UN-12; RISK-16 | **Not yet verified. Deferred — see [ADR-007](adr/ADR-007-dashboard-only-notification.md).** |

## 9. Decisions and open items

1. **Emergency calls are simulated.** Automatically dialling 911 is hard to do responsibly: telephony providers restrict it, a false call has real consequences, and a portfolio project must never place one. The system calls a dispatch interface, and this project supplies a simulator. To be recorded as an ADR when the design is written.
2. **Numbers in brackets** are placeholders. Detection and false-alarm targets are set after the first model evaluation. Response timeout and alert latency need clinician input.
3. **Safe zone is a circle** (centre and radius) in the first version. Other shapes become new requirements if needed.
4. **Response timeout is fixed** at [2 minutes]. Making it configurable is not required yet.
5. **Detection window.** Interview 1 found that a fall takes under three seconds (finding F-01). The detector's input window must be short enough to contain the fall and its impact rather than averaging them away. The exact window is a tuning decision recorded with the model, driven by this finding.
6. **Open:** nothing yet requires an audible alert on a device in the home. Finding F-03 suggests one may help, but it must not become the only delivery path. Pending the next interview.

## 10. Revision history

| Version | Date | Change |
| --- | --- | --- |
| 0.1 | 2026-11-05 | First draft: 6 fall and 7 wandering requirements |
| 0.2 | 2026-11-05 | REQ-FALL-01 and 02 updated for interview findings F-01 to F-03; detection-window note added |
| 0.3 | 2026-11-05 | Risk IDs traced; REQ-SYS-01, REQ-SEC-01, REQ-SEC-02 and REQ-AI-01 added |
| 0.4 | 2026-11-05 | REQ-AI-02 added, from the summary validator design in [ADR-005](adr/ADR-005-grounded-llm-summary.md) |
| 0.5 | 2026-11-05 | REQ-SEC-03 added: caregivers must authenticate. See [ADR-006](adr/ADR-006-identity-cookie-auth.md) |
| 0.6 | 2026-11-05 | REQ-SEC-04 added: readings accepted only from a paired, authenticated device |
| 0.7 | 2026-11-05 | REQ-NOTIFY-01 added: notification must not depend on an open session. Deferred, and recorded as unverified |
| 0.8 | 2026-11-06 | REQ-CFG-01 to 05 added. REQ-FALL-04 and REQ-FALL-05 had been showing as verified while only the honouring of those settings was tested and nothing could change them |
| 0.9 | 2026-11-06 | REQ-FALL-07 and REQ-FALL-08 added. Acknowledgement had been assumed by REQ-FALL-03 without any requirement stating that a caregiver can perform it, and the endpoint was built against that assumption. Reviewing the data behind an alert was not specified at all |
| 1.0 | 2026-11-06 | REQ-FALL-09 added: a caregiver can see alert counts for a resident over a date range they choose |
| 1.1 | 2026-11-06 | REQ-CFG-01 amended with an upper bound of 10 on the escalation limit. REQ-CFG-06 and REQ-CFG-07 added: a resident can set an emergency contact number and look up a caregiver by exact email before assigning them |
