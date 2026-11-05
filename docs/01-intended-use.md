# 01. Intended use and regulatory classification

| | |
| --- | --- |
| **Product** | ElderSafe: fall and wandering detection for older adults and their caregivers |
| **Document status** | Draft v0.1 |
| **Date** | 2026-11-05 |
| **Author** | Repository owner |
| **Related documents** | [Risk management](02-risk-management.md) (planned), [Software requirements](04-software-requirements.md) (planned), [Privacy](07-privacy.md) (planned) |

> **Portfolio project. Not a medical device. Synthetic data only.**
> This document is written the way a regulated software team would write it, to show the reasoning. It is not a regulatory submission and not legal advice. Classification statements below are the author's working assessment and should be verified against the current guidance before any real-world use.

## 1. Intended use

ElderSafe alerts a caregiver when a monitored older adult may have fallen or has left a defined safe zone. It supports routine supervision. It does not replace it.

## 2. Intended users and use environment

| User | Role in the system | Setting |
| --- | --- | --- |
| Family caregiver | Receives alerts, acknowledges them, checks on the resident | Home |
| Personal support worker (PSW) | Receives alerts, responds, acknowledges | Home care, long-term care |
| Nurse or charge nurse | Receives escalated alerts, reviews shift summaries | Long-term care, retirement residence |
| Administrator | Manages residents, safe zones and caregiver assignments | Care provider or the family |

Users are assumed to be adults with basic smartphone or web-browser skills. No clinical training is required to acknowledge an alert.

## 3. Patient population

Older adults who live at home or in a care setting, including people living with dementia who are at risk of wandering. The resident wears a device (simulated in this project) and does not operate the system.

## 4. What the software does

1. Receives accelerometer and GPS readings from a wearable device.
2. Detects a probable fall from motion data using a trained model.
3. Raises a wandering alert when a resident stays outside a safe zone for several consecutive readings.
4. Raises a "no data" alert when a device stops reporting.
5. Shows alerts to caregivers live, requires acknowledgement, and escalates unacknowledged alerts to a second caregiver.
6. Keeps an audit log of who viewed and acknowledged resident data.
7. Summarizes recent events for shift handoff, using only stored events (planned, AI-assisted).

## 5. Out of scope

- No diagnosis of any condition, including dementia or the cause of a fall.
- No treatment or medication recommendations.
- No replacement for emergency services. A caregiver must call 911 when a resident needs urgent help.
- No continuous clinical vital-sign monitoring (heart rate, oxygen and similar).
- No guarantee of detecting every fall or every exit from a safe zone.
- No real resident data. This project uses synthetic and publicly available research data only.

## 6. Regulatory assessment (working assessment)

**Would this be a medical device in Canada?** Health Canada regulates software as a medical device (SaMD) when it is intended to be used for a medical purpose, such as diagnosing, treating, mitigating or preventing a disease or condition, or for monitoring in support of such a purpose. Health Canada's guidance on [Software as a Medical Device](https://www.canada.ca/en/health-canada/services/drugs-health-products/medical-devices/application-information/guidance-documents/software-medical-device-guidance-document.html) sets out how it applies that test.

Working position for this project:

- A fall alert on its own is a safety and supervision aid. It does not diagnose or treat a disease.
- The wandering feature is aimed at people with dementia, a disease population. That moves the product closer to the medical-device boundary, so the project is treated *as if* it were a regulated device.
- The decision depends on the claims a manufacturer makes. Marketing it as monitoring a disease population would strengthen the case that it is a device.

**Treatment in this repository.** Whatever the formal answer, ElderSafe is developed under a lightweight version of a medical-software quality process: a risk analysis, traceable requirements, verification evidence and controlled releases.

## 7. Software safety classification (IEC 62304)

IEC 62304 assigns software to one of three safety classes by the worst harm the software could contribute to:

| Class | Meaning |
| --- | --- |
| A | No injury or damage to health is possible |
| B | Non-serious injury is possible |
| C | Death or serious injury is possible |

Two points about how this test works, because both are easy to get wrong:

1. **The class does not depend on whether the software diagnoses or treats.** That question decides whether something is a medical device at all (section 6). The safety class asks only what harm could follow from a software failure. Software that performs no diagnosis can still be Class C.
2. **Probability is not used here.** IEC 62304 assumes the software fails, then asks how bad the result could be. The word in the class definitions is *possible*, not *likely*. Probability is used later, in the risk file under ISO 14971, to score each hazard and justify its controls.

### Assessment: Class C

**Hazardous situation.** The software fails to raise an alert for a fall or for a resident leaving the safe zone, so nobody is told and nobody comes.

**Possible harm.** An older adult lies undiscovered for hours. A long lie can lead to pressure injury, dehydration, rhabdomyolysis or hypothermia. For an undetected exit from a safe zone, a resident with dementia may be outdoors and unfound, which in a Canadian winter can be fatal. Both sit squarely in "death or serious injury is possible."

### Why not Class A

The argument for Class A: ElderSafe does not diagnose, treat or prevent any disease. Its output is a notification, and every consequence passes through a caregiver's judgment, so the software itself never acts on a resident.

Why it is rejected:

- The diagnosis-and-treatment test belongs to section 6, not to this section. It does not bear on the safety class.
- The human in the loop is not a safeguard against this failure mode, because the failure *is* that the human was never told. A caregiver cannot exercise judgment about an alert that never arrived.
- Class A requires that no injury is possible after a software failure. That would need an external risk control catching every missed alert in time. This product has none it can rely on in every setting.

### Why not Class B

The argument for Class B: a missed alert does not necessarily kill anyone. The resident may get up unaided, be found on the next visit, or come to no lasting harm.

Why it is rejected:

- Class B covers cases where only *non-serious* injury is possible. Class C is reached by death **or** serious injury, and serious injury is plainly possible here. Showing that death is not certain does not reach Class B.
- The test is possibility, not likelihood. That a bad outcome is uncommon belongs in the risk file, where probability is scored, not in this classification.
- A defensible Class B would need the intended use narrowed so the long-lie case is excluded, for example facility-only use with documented rounds every 30 minutes. This product is intended for private homes as well (section 2), where no such control exists.

### Consequence of Class C

The project applies the full IEC 62304 development activities, including architectural design, detailed design for each software unit, and unit-level verification. The practical cost here is small, because the documentation and test discipline were planned anyway.

If the intended use or the external controls change, this classification must be revisited.

## 8. Data handled

| Data | Why it is needed | Sensitivity |
| --- | --- | --- |
| Resident identifier and display name | Show who an alert is about | Personal information |
| Accelerometer readings | Detect falls | Personal health information |
| GPS location | Detect wandering | Personal health information, and sensitive location data |
| Alert, acknowledgement and audit events | Prove who was told and who responded | Personal health information |

Demo data is synthetic. A real deployment would be subject to Ontario's PHIPA and Canada's PIPEDA. Retention, access and residency decisions will be recorded in `docs/07-privacy.md` (planned).

## 9. Assumptions and known limitations

- The wearable is worn and charged. A silent device raises a "no data" alert, but it cannot detect a fall.
- GPS accuracy varies, especially indoors and near the edge of a safe zone.
- Detection accuracy depends on the training data. Results on a public research dataset do not predict accuracy in real homes.
- Alerts depend on network connectivity and on a caregiver seeing them in time.

## 10. Traceability

| This document feeds | Through |
| --- | --- |
| Risk management file | Hazards for each intended function in section 4 |
| Software requirements | Requirements derived from section 4 and the risk controls |
| README disclaimer | Section 5, "Out of scope" |

## 11. Revision history

| Version | Date | Change |
| --- | --- | --- |
| 0.1 | 2026-11-05 | First draft |
