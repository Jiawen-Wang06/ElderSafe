# 03. User needs

| | |
| --- | --- |
| **Product** | ElderSafe |
| **Document status** | Draft v0.2. One interview done. Needs still marked *Hypothesis* are not yet validated. |
| **Date** | 2026-11-05 |
| **Related documents** | [Intended use](01-intended-use.md), [Software requirements](04-software-requirements.md) |

> **Portfolio project. Not a medical device. Synthetic data only.**
> User needs describe what people need to accomplish and know. They do not say how the software works. Requirements in document 04 do that, and each requirement points back to a need here.

## 1. Interview record

Complete one row per interview. Record the role, never a name. Do not record any resident or patient details, even if the interviewee offers them.

| # | Interviewee role | Setting | Date | Notes |
| --- | --- | --- | --- | --- |
| 1 | Registered nurse | Elder care | 2026-11-05 | Findings in section 2 |

A second source is the author's own family experience of a fall at home. It is recorded in section 2 as an anonymized account, because it illustrates a failure mode the interview also raised.

## 2. Findings

### F-01. A fall is over in about three seconds

> "Most of the time, elders fall down in less than three seconds." — Interview 1

**Design implications.**

- The event the detector must recognize is brief. The model's input window has to be short enough to contain the fall and its impact, rather than averaging it away across a longer window. The window length is a tuning decision for the detector, and this finding is its input.
- There is no useful warning period. The system cannot prevent a fall; it can only shorten the time until someone comes.

### F-02. A resident often cannot call for help after falling

> "If they live alone, a lot of the time they won't be able to reach their phone to call for help after they fall." — Interview 1

**Design implications.**

- Any design that depends on the resident acting after the fall, such as pressing a button or reaching a phone, fails in exactly the cases that matter most. This is the core justification for automatic detection.
- Detection must not require confirmation from the resident. The system cannot wait for a person on the floor to dismiss or confirm an alert before notifying anyone.

### F-03. Another person in the home is not a reliable safeguard

An anonymized account from the author's family: a resident fell at home while a co-resident was present. The co-resident was asleep, and has age-related hearing loss, so the resident's calls for help went unheard.

**Design implications.**

- "Lives with someone" cannot be treated as a risk control. Presence is not supervision. A second person may be asleep, in another room, or unable to hear.
- An alert must reach a caregiver through a channel that does not depend on anyone overhearing anything, and it must escalate when the nearest person does not respond. This is direct evidence for the escalation requirements and against assuming a household member will notice.
- This account also bears on the safety classification in [intended use](01-intended-use.md) section 7. An argument for a lower class rests on an external control catching the failure. Here the apparent external control, another adult in the house, was present and did not work.

## 3. Interview guide (about 30 minutes)

Open with: "I'm building a portfolio project about fall and wandering alerts. I want to learn how this works in practice. I don't need names or patient details, and nothing you say will identify you." Ask open questions and let the interviewee talk. Write down their exact words.

**Context**
1. What is your role, and what kind of setting do you work in?
2. How do you currently find out that a resident has fallen or gone missing?

**Falls**
3. Walk me through the last time a fall was noticed late. What happened?
4. How quickly do you need to know about a fall, and does the answer change at night?
5. What do you do in the first minute after you hear about one?

**Wandering**
6. How do you handle residents who might leave a unit or the home? What works and what doesn't?
7. How far or how long outside the usual area should trigger concern?

**Alerts and alarm fatigue**
8. How do you feel about alarms and alerts you already deal with? Which ones do people ignore, and why?
9. How many false alarms would be too many?
10. If an alert isn't answered, who should hear about it next, and after how long?

**Handoff and records**
11. What do you tell the next shift about a resident's events? How do you pass it on today?
12. What do you need written down after an incident?

**Privacy and trust**
13. Who should and shouldn't see a resident's location or event history?
14. What would make you distrust a system like this?

**Close.** "What did I not ask that I should have?" Then thank them and ask whether they'd read a short summary of what you heard.

## 4. Needs register

**Status key:** *Hypothesis* means it was written before any interview. *Validated* means a source stated it. *Changed* or *Dropped* means a source contradicted it. Dropped needs stay in the table, with the reason.

| ID | Need | In their words | Source | Priority | Status |
| --- | --- | --- | --- | --- | --- |
| UN-01 | Know quickly when a resident may have fallen, including at night | "Most of the time, elders fall down in less than three seconds." | Interview 1 (F-01) | Must | Validated |
| UN-02 | Know when a resident has left a safe area | | Hypothesis | Must | Hypothesis |
| UN-03 | Not be flooded by false alarms | | Hypothesis | Must | Hypothesis |
| UN-04 | Be sure an alert reaches someone, and escalates if nobody responds | | Interview 1, family account (F-03) | Must | Validated |
| UN-05 | Know when the device has stopped working or reporting | | Hypothesis | Should | Hypothesis |
| UN-06 | See only the residents I am responsible for | | Hypothesis | Should | Hypothesis |
| UN-07 | Have a record of who responded to each alert and when | | Hypothesis | Should | Hypothesis |
| UN-08 | Hand over recent events to the next shift quickly and accurately | | Hypothesis | Could | Hypothesis |
| UN-09 | Decide for myself how far an unanswered alert travels, and whether it ends in an emergency call | | Hypothesis | Should | Hypothesis |
| UN-10 | Set the area a resident can move around in safely | | Hypothesis | Must | Hypothesis |
| UN-11 | Get help to a resident who cannot summon it themselves | "If they live alone, a lot of the time they won't be able to reach their phone to call for help after they fall." | Interview 1 (F-02) | Must | Validated |
| UN-12 | Have an alert reach a caregiver even when another person is in the home but unaware | | Family account (F-03) | Must | Validated |

Priority uses three levels: *Must* (the product is unsafe or unusable without it), *Should*, *Could*.

## 5. Open questions for the next interview

The first interview covered falls. These remain unanswered, and the needs that depend on them are still hypotheses.

- Wandering: what distance or duration outside the usual area should raise an alert (UN-02)?
- False alarms: how many per week before caregivers start ignoring them (UN-03)?
- Escalation: who should be told second, and after how long (UN-04 timing)?
- Emergency calls: would a caregiver ever want an automatic 911 call, or is that always a human decision (UN-09)?
- Night versus day: does the acceptable response time change overnight?

## 6. What to do after each interview

1. Add a findings subsection with the quotes, then update the register: fill in the words, mark each need validated, changed or dropped, and add any new needs.
2. Note what surprised you. A surprise means a hypothesis was wrong, and that is the useful part.
3. Check the intended-use document. If an interview contradicts section 4 or 5 there, revise it and record the change in its revision history.
4. Check that every requirement in `04-software-requirements.md` still traces to a need that survived.

## 7. Revision history

| Version | Date | Change |
| --- | --- | --- |
| 0.1 | 2026-11-05 | First draft: interview guide and hypothesis register |
| 0.2 | 2026-11-05 | Interview 1 (registered nurse) and a family account. Findings F-01 to F-03. UN-01, UN-04 validated; UN-11 and UN-12 added. |
