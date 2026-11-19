# ADR-008: Detect falls with a model trained in Azure Machine Learning, run in-process as ONNX

**Status:** Accepted. Amends [ADR-002](ADR-002-rule-based-detector.md).
**Date:** 2026-11-18

## Context

ADR-002 chose threshold rules over a trained model for two reasons: training would blow the build budget, and a quickly trained model would come with an accuracy figure that looks like evidence and is not. Its "revisit when" condition was a licensed dataset and the time to split it by subject, train and evaluate properly.

That condition is now met. There is still no way to collect real data in this project, so the model is trained on two public datasets of falls simulated by volunteers in a lab:

| Dataset | Use | Licence |
| --- | --- | --- |
| UCI *Simulated Falls and Daily Living Activities* (Özdemir and Barshan, 2014), waist sensor | Training and cross-validation | CC BY 4.0 |
| UMAFall (Casilari and Santoyo-Ramón, 2018), waist sensor | External test only, never seen in training or tuning | CC BY-NC 4.0 |

The project is a non-commercial portfolio piece, which is what the UMAFall licence allows. Neither dataset is committed to the repository.

The rule-based detector was evaluated the same way, so the two can be compared on equal terms. Full results are in the [model card](../models/fall-model-card.md). Measured per recording, the main figures are:

| On UMAFall (held out) | Rule (ADR-002) | Model |
| --- | --- | --- |
| Falls detected | 31% | 85% |
| Daily-activity recordings with a false alarm | 1.7% | 3.8% |

The rule misses most recorded falls. Its thresholds were tuned against the project's own simulator, which ADR-002 warned would measure the simulator and not the detector, and these figures confirm it.

## Options

1. **Keep the rule.** It is explainable and already tested, but it misses about two falls in three on data it was not tuned on.
2. **Host the model on an Azure ML managed endpoint and call it per window.** This is the usual tutorial shape. It adds a network call, an outage mode and a running cost, at exactly the point where a missed alert is the worst outcome (RISK-01).
3. **Train and register the model in Azure ML, export it to ONNX, and run it inside the API.** Azure ML holds the data versions, the training job, the metrics and the registered model. The API loads the file and scores in-process.

## Decision

Option 3.

- **Training.** `tools/ml/train.py` runs as an Azure ML command job against the registered data assets `uci-falls-waist:1` and `umafall:1`. It trains LightGBM on 8 features computed over 4-second windows at 20 Hz. It chooses the decision threshold by subject-wise cross-validation for 95% sensitivity, then reports on UMAFall without retuning. The model is registered as `eldersafe-fall`, tagged with its SHA-256.
- **Runtime.** `OnnxFallDetector` implements the existing `IFallDetector`. `Evaluate` still does no I/O, so the interface's purity contract holds and nothing in alerting or escalation changes.
- **Integrity.**
  - The API refuses to start if the model file's SHA-256 does not match its metadata, or if the model expects different features from the ones `FallFeatures` computes.
  - Every verdict names the model version (`eldersafe-fall@<first 12 hex of the hash>`), and that reason is written to the audit log. Any alert can therefore be traced to a training run.
- **Training-serving parity.** The features are defined twice: `features.py` for training and `FallFeatures.cs` for the API. A test feeds windows saved by the training run through the C# code and requires the same features and the same score.
- **Short windows.** The model scores a window only if it covers at least 3 s with at least 80% of the expected readings. Otherwise the verdict is `NotEnoughData`. The 3 s floor exists because the window is cut from the server clock, so latency always trims a little; measured on UMAFall, scoring windows 1 s short costs 2.7 points of sensitivity and adds no false alarms.
- **Selection.** `FallDetection:Detector` chooses `Onnx` or `Threshold`. **The default is `Onnx`.** The rule stays in the codebase, tested, as a configured fallback and as the reference the alert trace draws.

## Consequences

**Good**

- Far more of the held-out falls are detected, and the figure comes from a subject-wise split and an external dataset rather than the project's own simulator.
- No network call or hosted endpoint sits on the alert path, and there is no running cost. Azure ML is used for what it is good at here: versioned data, reproducible jobs, logged metrics and a model registry.
- Each alert names the model that raised it, and the hash ties that model to its training run.

**Bad**

- **The model raises more false alarms than the rule:** about twice as many on held-out daily activity. That trades RISK-01 against RISK-02, and the choice favours detecting falls.
- **No figure describes real falls of older people.** Both datasets are falls simulated by young adults on mats. REQ-FALL-11 stays deferred:
  - its targets refer to real recorded falls;
  - the false-alarm rate measured here is far above its ceiling of one per resident per day;
  - the evaluation is not run in CI.
- **The model is harder to explain than the rule.** A caregiver can no longer see which threshold a reading crossed. The alert trace (REQ-FALL-08) now reports the detector and its recorded reason, and draws the rule's thresholds only as a labelled reference.
- **The features are defined in two languages.** The parity test guards this, but only for the windows saved in its fixture.
- **The model, its metadata and the parity fixture must come from the same training run.** Replacing one without the others fails the hash check or the parity test, which is intended.
- **ONNX Runtime is a new SOUP item** ([SOUP list](../06-soup.md)).

## Revisit when

- Recordings of real falls by older people are available. REQ-FALL-11's evaluation can then be run and gated in CI.
- The false-alarm rate on daily activity needs to come down. Options include more varied daily-activity data, a longer stillness context, or a second-stage check.
