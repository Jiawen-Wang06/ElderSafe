# Model card: eldersafe-fall

| | |
| --- | --- |
| **Model** | `eldersafe-fall`, version 1, registered in Azure Machine Learning (workspace `mlw-eldersafe`) |
| **SHA-256** | `467b5555955818f1b998a778628a6043e2f930cb503e0f6e2bb298e1444ca724` |
| **Shown in verdicts as** | `eldersafe-fall@467b55559558` |
| **Type** | LightGBM binary classifier over 8 hand-built features, exported to ONNX |
| **Runs in** | `OnnxFallDetector`, in-process via ONNX Runtime ([ADR-008](../adr/ADR-008-trained-fall-model.md)) |
| **Training code** | `tools/ml/` (`train.py`, `features.py`, `datasets.py`, `job.yml`) |
| **Date** | 2026-11-18 |

> **Portfolio project. Not a medical device.** The model was trained and tested only on falls simulated by volunteers. No figure here describes real falls of older people.

## 1. What it does

The model takes 4 seconds of waist-worn accelerometer readings at 20 Hz and returns the probability that the window contains a fall. A probability at or above **0.9961** is reported as a fall.

A "fall" here means the wearer fell and stayed down. Falls the volunteer recovered from, standing up again within the recording, were excluded from training. A product alert matters most when the person cannot get up (RISK-01).

## 2. Data

| Dataset | Role | Wearers | Recordings used | Licence and citation |
| --- | --- | --- | --- | --- |
| UCI *Simulated Falls and Daily Living Activities*, waist sensor (Xsens MTw, 25 Hz) | Training, subject-wise cross-validation, threshold choice | 17 volunteers | 1,477 falls, 1,482 daily activities | CC BY 4.0. Özdemir, A. T. and Barshan, B. (2014). *Detecting falls with wearable sensors using machine learning techniques.* Sensors 14(6), 10691–10708 |
| UMAFall, waist sensor (about 20 Hz) | External test only | 18 volunteers with waist data | 186 falls, 424 daily activities | CC BY-NC 4.0. Casilari, E. and Santoyo-Ramón, J. A. (2018). *UMAFall: Fall detection dataset.* figshare |

**Preparation:**
- Both datasets are converted to g and resampled to 20 Hz, the rate the simulator and a wearable would send.
- Fall windows are placed with the impact 1 to 2 s into the window. Daily-activity windows slide over the whole recording.
- Four UCI fall types in which the volunteer recovers (codes 905, 906, 914, 916) are excluded.
- 136 UMAFall recordings have no waist sensor data and are skipped.

**Handling:**
- Neither dataset is committed to the repository.
- The UCI archive also contains volunteer information files with personal details. These were never extracted or uploaded.
- Only the waist-sensor folder was uploaded to Azure ML, as data asset `uci-falls-waist:1`. UMAFall is data asset `umafall:1`.

## 3. Features

Computed identically by `tools/ml/features.py` (training) and `FallFeatures.cs` (API). A test holds the two to the same values.

| Feature | Meaning |
| --- | --- |
| `mag_min`, `mag_max`, `mag_mean`, `mag_std` | Acceleration magnitude over the window |
| `peak_minus_dip_s` | Time from the lowest reading to the highest. Free fall comes just before impact |
| `tail_mag_mean`, `tail_mag_std` | Magnitude over the last 1.5 s, to detect lying still |
| `orientation_change_deg` | Angle between the mean acceleration vector in the first 1 s and in the last 1.5 s. Lying down moves gravity to another axis |

Every feature describes a part of a fall that a reader can name. Training on raw samples was not tried.

## 4. Training and threshold

- **Model:** LightGBM, 200 trees of up to 15 leaves, learning rate 0.05.
- **Cross-validation:** grouped 5-fold on UCI, so no volunteer's recordings appear in both training and validation for the same fold.
- **Threshold:** chosen from the cross-validated scores as the highest that still detects 95% of falls. Detection is counted per recording: a recording counts as detected if any window in it scores at or above the threshold. The threshold was fixed before UMAFall was scored.
- **Export:** ONNX via `onnxmltools`. A parity check in the training script compares ONNX Runtime's probabilities with LightGBM's before the files are written.
- **Tracking:** the run, its metrics and artifacts are logged to Azure ML with MLflow. The data asset versions are recorded with the model.

## 5. Results

Per recording, with windows slid every 0.25 s, as the API sees them. The same evaluation was run on a Python port of the rule-based detector (`tools/ml/rule_baseline.py`) using the thresholds in `appsettings.json`.

| | Model, UCI cross-validated | Rule, UCI | **Model, UMAFall (held out)** | **Rule, UMAFall (held out)** |
| --- | --- | --- | --- | --- |
| Falls detected | 95.1% | 69.6% | **85.5%** | **31.2%** |
| Daily-activity recordings with any false alarm | 0.9% | 0.5% | **3.8%** | **1.7%** |
| False alarms per hour of recorded daily activity | 1.7 | 0.8 | **9.2** | **4.0** |

**Reading these figures:**
- **UMAFall is the honest number.** It comes from different volunteers, a different sensor, a different lab and a different protocol, none of which influenced training or the threshold. The cross-validated figure is optimistic by construction, because the threshold was chosen on it.
- **The per-hour rates overstate daily life.** The daily-activity recordings are short bursts of deliberately varied movement (jogging, hopping, stairs, bending), not hours of ordinary living. They still show the model is far from REQ-FALL-11's ceiling of one false alarm per resident per day.
- **The model and the rule trade off differently.** The model detects almost three times as many held-out falls and raises about twice as many false alarms.

**Sensitivity to short windows.** Windows 1 s shorter than training cost 2.7 points of sensitivity on UMAFall (82.8% instead of 85.5%) and no change in false alarms. This is why the API scores windows of 3 s or more (`FallModel:MinimumWindowSeconds`).

## 6. Limitations

- **Simulated falls by young adults.** The volunteers fell deliberately onto mats. Older people fall differently: slower collapses, slides down furniture, falls with an arm out. A model that has never seen these will miss some of them, and no figure here says how many (RISK-01).
- **One body position.** The model is trained on a waist sensor only. A wrist-worn or pendant device would need a different model.
- **False alarms above any acceptable product rate** (RISK-02). REQ-FALL-11 stays deferred.
- **Recovery falls are out of scope by design.** A person who falls and gets up is not alerted for.
- **The demo shows typical falls only.** The dashboard simulator varies each fall within the middle half (25th to 75th percentile) of the recorded falls for lowest magnitude, peak, free-fall duration and orientation change. The demo therefore always detects them. It is a demonstration, not evidence: a quarter of recorded falls are milder than the simulator on each of those measures.

## 7. Reproducing and replacing the model

1. Register the datasets as Azure ML data assets, and submit `tools/ml/job.yml` (`az ml job create -f tools/ml/job.yml`), or run `train.py` locally with `--uci`, `--uma` and `--out`.
2. Register the job's `outputs/` as a new model version.
3. Copy three files from **the same run**: `model.onnx` and `model.json` into `src/ElderSafe.Api/Models/fall/`, and `fall_model_fixture.json` into `tests/ElderSafe.Api.Tests/Detection/Fixtures/`.
4. Run the tests. The parity test fails if the fixture and model come from different runs. The API refuses to start if `model.onnx` does not match the hash in `model.json`.
5. Update this card's hash, version and results, and add a line to the revision history.

## 8. Revision history

| Version | Date | Change |
| --- | --- | --- |
| 1 | 2026-11-18 | First model: LightGBM on UCI waist data, tested on UMAFall |
