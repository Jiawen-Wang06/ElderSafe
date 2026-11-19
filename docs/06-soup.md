# 06. Software of unknown provenance (SOUP)

| | |
| --- | --- |
| **Product** | ElderSafe |
| **Document status** | Draft v0.1 |
| **Date** | 2026-11-18 |
| **Related documents** | [Risk management](02-risk-management.md), [Architecture](05-architecture.md), [Fall model card](models/fall-model-card.md) |

> **Portfolio project. Not a medical device.**

IEC 62304 asks for each SOUP item to be identified, with the requirements it is relied on for and the known anomalies that could matter. This list covers third-party software that **ships with and runs in** ElderSafe.

**Tools that only produce artifacts are not SOUP.** These are the training libraries (LightGBM, scikit-learn, onnxmltools, MLflow), the test frameworks and the build tooling. Their output is verified instead:
- the model by its evaluation, the hash check and the parity test;
- the build by the test suite.

Their versions are pinned in `tools/ml/conda.yml` and the project files.

## 1. Detection path

These matter most, because a defect here is a missed or false fall alert (RISK-01, RISK-02).

| SOUP | Version | Used for | Relied on for | How the reliance is checked |
| --- | --- | --- | --- | --- |
| Microsoft.ML.OnnxRuntime | 1.32.0 | Running the fall model in-process (`OnnxFallDetector`) | Computing the model's probability exactly as the training run did | <ul><li>The training script compares ONNX Runtime's output with LightGBM's before saving the model.</li><li>`REQ-FALL-01 computes the same features and score as the training pipeline` repeats the comparison in the API on every test run.</li><li>A model whose hash or features do not match its metadata stops startup.</li></ul> |
| The `eldersafe-fall` model file | Version 1, SHA-256 `467b5555…` | The decision itself | Its measured behaviour, recorded in the model card | <ul><li>SHA-256 verified at load.</li><li>The model version is written into every verdict and the audit log.</li><li>Evaluation results are in the [model card](models/fall-model-card.md).</li></ul> |

The model is not third-party software, but it is a component this project did not write line by line, and its behaviour is known only by measurement. That is why it is listed here.

**Known anomalies:**
- ONNX Runtime's issue tracker has not yet been reviewed for anomalies at this version. Until it is, the parity test is the evidence that the runtime computes what training computed, for the windows in its fixture.
- Floating-point results can differ in the last digits between runtimes. The parity test compares to 4 decimal places, which is well inside the margin between typical scores and the threshold.

## 2. Rest of the system

| SOUP | Version | Used for |
| --- | --- | --- |
| ASP.NET Core, including SignalR and Identity | .NET 10 (packages 10.0.14) | Web host, live alerts, authentication |
| Entity Framework Core with SQLite provider | 10.0.14 | Storage ([ADR-001](adr/ADR-001-sqlite-over-postgres.md)) |
| React and React DOM | 19 | Dashboard |
| @microsoft/signalr | 10 | Dashboard's live connection |
| react-router | 8 | Dashboard navigation |
| Base UI, shadcn/ui components, Tailwind CSS, lucide-react | See `package.json` | Dashboard presentation |

These are relied on for their documented behaviour. They are exercised end to end by the integration tests, which run the real host against a real SQLite file. Exact versions are pinned by the lock files and project files. Anomaly review for them has not been done in this project.

## 3. Revision history

| Version | Date | Change |
| --- | --- | --- |
| 0.1 | 2026-11-18 | First list, written when the trained fall model and ONNX Runtime were added ([ADR-008](adr/ADR-008-trained-fall-model.md)) |
