<p align="center">
  <img src="docs/media/headerImg.png" alt="ElderSafe" width="100%">
</p>

| | |
| --- | --- |
| **Backend** | ![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white) ![ASP.NET Core](https://img.shields.io/badge/ASP.NET%20Core-SignalR%20%C2%B7%20Identity-512BD4?logo=dotnet&logoColor=white) ![SQLite](https://img.shields.io/badge/SQLite-EF%20Core-003B57?logo=sqlite&logoColor=white) |
| **Frontend** | ![React](https://img.shields.io/badge/React-19-61DAFB?logo=react&logoColor=black) ![TypeScript](https://img.shields.io/badge/TypeScript-6-3178C6?logo=typescript&logoColor=white) ![Vite](https://img.shields.io/badge/Vite-8-646CFF?logo=vite&logoColor=white) ![Tailwind CSS](https://img.shields.io/badge/Tailwind%204-shadcn%2Fui-06B6D4?logo=tailwindcss&logoColor=white) |
| **ML** | ![Python](https://img.shields.io/badge/Python-3.13-3776AB?logo=python&logoColor=white) ![LightGBM](https://img.shields.io/badge/LightGBM-classifier-2E7D32) ![ONNX Runtime](https://img.shields.io/badge/ONNX%20Runtime-in--process-005CED?logo=onnx&logoColor=white) ![Azure Machine Learning](https://img.shields.io/badge/Azure%20Machine%20Learning-model%20training%20%C2%B7%20registry-0078D4) ![MLflow](https://img.shields.io/badge/MLflow-tracking-0194E2?logo=mlflow&logoColor=white) |
| **LLM** | ![Microsoft Foundry](https://img.shields.io/badge/Microsoft%20Foundry-LLM%20shift%20summary-0078D4) ![Azure OpenAI](https://img.shields.io/badge/Azure%20OpenAI-GPT--4.1--mini-412991) |
| **Delivery** | ![Docker](https://img.shields.io/badge/Docker-GHCR-2496ED?logo=docker&logoColor=white) ![Azure App Service](https://img.shields.io/badge/Azure-App%20Service-0078D4) ![GitHub Actions](https://img.shields.io/badge/CI-GitHub%20Actions-2088FF?logo=githubactions&logoColor=white) |
| **Quality** | ![xUnit](https://img.shields.io/badge/xUnit-187%20tests-512BD4) ![Vitest](https://img.shields.io/badge/Vitest-71%20tests-6E9F18?logo=vitest&logoColor=white) ![IEC 62304](https://img.shields.io/badge/IEC%2062304-Class%20C%20framing-B71C1C) |

**Fall detection for older adults: a trained model decides, caregivers are alerted live, and unanswered alerts escalate.**

> Portfolio project, built as if for a regulated medical domain. **Not a medical device. Public and synthetic data only.**

**[Live demo](https://eldersafe-dtc4hucbascmcgd8.canadaeast-01.azurewebsites.net)**: sign in as `demo.caregiver@example.invalid` / `Demo-Passw0rd!`, press **Simulate fall**, and watch the alert arrive, escalate and get acknowledged. The first request after a quiet period may take a few seconds.

![ElderSafe demo: simulating a fall, acknowledging it, and viewing the summary](docs/media/demo.gif)

## Highlights

- **A trained model that beats the rules it replaced.** Raised fall detection on a dataset the model never saw (UMAFall) from **31.2% to 85.5%**, by training a **LightGBM** classifier on 8 explainable accelerometer features as an **Azure Machine Learning** job (tracked with **MLflow**, registered in the Azure ML model registry). It was validated with subject-wise 5-fold cross-validation, and its threshold was fixed before the held-out data was scored.
  - The trade-off is stated, not hidden: about twice the rules' false-alarm rate (9.2 vs 4.0 per hour of daily activity). The choice to favour catching falls is recorded against the risk file.
  - → [ADR-008](docs/adr/ADR-008-trained-fall-model.md), [model card](docs/models/fall-model-card.md)
- **Model serving with no network hop and a verifiable version.** Scored every reading window in-process, with the exact model version in each alert's audit entry, by exporting the model to **ONNX** and running it through **ONNX Runtime in .NET**. The API checks the model's SHA-256 hash at startup and refuses to start on a mismatch, and a parity test holds the Python and C# feature code to the same values.
- **An LLM feature that treats model output as untrusted.** Raised the handover summaries that flag a still-open alert from **3 of 7 to 7 of 7**, with **0 invented or missing alerts** in 10 live summaries per prompt version, by grounding **Azure OpenAI (GPT-4.1-mini, deployed in Microsoft Foundry)** in stored alerts only. A validator withholds any summary that invents or omits an escalated alert, the app falls back to a template when the model is rate-limited or down, and each summary records its prompt and model version.
  - → [ADR-005](docs/adr/ADR-005-grounded-llm-summary.md), including the live evaluation of prompt v1 against v2
- **Regulated-domain engineering.** Traced all **37 requirements** to user needs, hazards and tests, and scored **22 hazards** before and after controls, by applying **IEC 62304 Class C** framing and building a **CI gate** that fails the build when any requirement has no passing test and isn't deferred with a recorded reason. User needs come from an interview with a registered nurse.
  - → [risk file](docs/02-risk-management.md), [traceability matrix](docs/traceability.md)
- **Real-time alerting with escalation.** Delivered fall alerts to caregivers' dashboards live, and escalated any alert left unanswered for **2 minutes** to the next caregiver and then to a simulated emergency call, by building an **ASP.NET Core** API with **SignalR**, a background escalation worker behind a pure, unit-tested policy, and a **React 19 + TypeScript** dashboard.
- **Security by default.** Returned **404, never 403**, for another resident's data, so the API never confirms that a record exists, by enforcing relationship-based authorization on **ASP.NET Core Identity** cookie auth, with separate API-key authentication for devices. Secrets stay out of the repository, and the full history is scanned.
- **Shipped like production software.** Gated every pull request on **258 tests (187 xUnit + 71 Vitest)**, lint and the traceability check, by building **GitHub Actions** pipelines. The **Docker** image is started and smoke-tested (sign in, simulate a fall, confirm an alert) before that exact image is published to **GHCR**, where **Azure App Service** runs it.
  - → [ADR-009](docs/adr/ADR-009-container-deployment.md)

## How it works

```
Wearable (simulated) ──readings──▶ ASP.NET Core API ──SignalR──▶ React dashboard
                                     │
                    4 s window ──▶ IFallDetector ── ONNX model (default) | threshold rules
                                     │ fall
                                     ▼
                     Alert ─▶ escalate through caregivers every 2 min ─▶ simulated emergency call
                                     │
                     Shift summary ─▶ Azure OpenAI or template ─▶ validator ─▶ shown or withheld
```

One process, one SQLite file, one container. Domain decisions such as detection and escalation are pure classes behind interfaces, and side effects go through ports. Emergency calls are **simulated**: nothing in the system can place a real call.

## Run it locally

Requires the .NET 10 SDK and Node 22+.

```bash
cd src/ElderSafe.Api && dotnet run            # API on :5134, seeds demo data
cd src/ElderSafe.Dashboard && npm install && npm run dev   # dashboard on :5173
```

Or run the deployed image: `docker run -p 8080:8080 -e DemoData__Enabled=true -e Simulation__Enabled=true ghcr.io/jiawen-wang06/eldersafe:latest`

Demo accounts (password `Demo-Passw0rd!`): `demo.caregiver@example.invalid` and `demo.nurse@example.invalid`, the caregivers in escalation order, and `demo.resident@example.invalid`, who configures them.

<details>
<summary>Optional: turn on AI shift summaries</summary>

Without setup, summaries come from a template. To use an Azure OpenAI deployment instead, store its details as user secrets (in Azure, as App Service settings with `__` in place of `:`):

```bash
cd src/ElderSafe.Api
dotnet user-secrets set "ShiftSummary:AzureOpenAI:Endpoint" "https://<your-resource>.cognitiveservices.azure.com/"
dotnet user-secrets set "ShiftSummary:AzureOpenAI:Deployment" "<your-deployment-name>"
dotnet user-secrets set "ShiftSummary:AzureOpenAI:ApiKey" "<your-key>"
```
</details>

## Documentation

| Document | What it shows |
| --- | --- |
| [Intended use](docs/01-intended-use.md) | Why the software is IEC 62304 Class C rather than A or B |
| [Risk management](docs/02-risk-management.md) | 22 hazards, controls, and residual risk stated honestly |
| [User needs](docs/03-user-needs.md) | Findings from an interview with a registered nurse |
| [Requirements](docs/04-software-requirements.md) | 37 requirements; 12 deferred, each with a recorded reason |
| [Architecture](docs/05-architecture.md) and [ADRs](docs/adr/) | The design, and 9 decision records covering what was traded away |
| [Fall model card](docs/models/fall-model-card.md) | Data, licences, features, held-out results and limitations |
| [SOUP list](docs/06-soup.md) | Third-party software and how reliance on it is checked |
| [Traceability matrix](docs/traceability.md) | Generated from test results by CI on every run |

## Known limitations

Each limitation is stated in the docs rather than hidden:
- **No accuracy claim for real falls.** Every fall in the training and test data was simulated by a young adult, and the false-alarm rate is above any product target, so REQ-FALL-11 stays deferred.
- **Alerts reach the dashboard only.** There's no push or SMS, which makes this the largest residual risk ([ADR-007](docs/adr/ADR-007-dashboard-only-notification.md)).
- **Data resets when the container restarts,** because SQLite lives inside the container. There's no staging slot or Bicep yet.
- **Not built yet:** wandering detection, the "no data" alert, a device-pairing flow and FHIR export. All are specified, traced and marked deferred.

## Datasets and credit

The fall model exists because these research groups published their data. Thank you.

| Dataset | Used for | Licence |
| --- | --- | --- |
| **Simulated Falls and Daily Living Activities**: A. T. Özdemir and B. Barshan, Bilkent University. [UCI Machine Learning Repository](https://archive.ics.uci.edu/dataset/455/simulated+falls+and+daily+living+activities+data+set), [doi:10.24432/C52028](https://doi.org/10.24432/C52028). Paper: *Detecting Falls with Wearable Sensors Using Machine Learning Techniques*, Sensors 14(6), 2014 ([PMC4118339](https://pmc.ncbi.nlm.nih.gov/articles/PMC4118339)) | Training and cross-validation | CC BY 4.0 |
| **UMAFall**: E. Casilari and J. A. Santoyo-Ramón, University of Málaga. [figshare, doi:10.6084/m9.figshare.4214283](https://doi.org/10.6084/m9.figshare.4214283). Paper: *UMAFall: A Multisensor Dataset for the Research on Automatic Fall Detection*, Procedia Computer Science 110, 2017 | Held-out external test only | CC BY-NC 4.0 |

Neither dataset is redistributed in this repository. Only the waist-sensor recordings were used, and the volunteers' personal-information files were never extracted. See the [model card](docs/models/fall-model-card.md).

## Working agreements

- `main` is protected, and every change goes through a pull request.
- PR titles follow Conventional Commits.
- Every test carries its requirement ID, which is what lets CI regenerate the traceability matrix.

## License

All rights reserved. See [LICENSE](LICENSE). This repository is public for review, not for reuse.
