# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

ElderSafe is a fall-detection portfolio project built as if for a regulated medical domain (IEC 62304 Class C framing). It is **not a medical device and uses synthetic data only**. The documentation in `docs/` (intended use, risk file, requirements, architecture, ADRs, traceability) is half the point of the repo. Code changes are expected to keep it consistent.

## Commands

Requires the .NET 10 SDK and Node 22+. The solution file is `ElderSafe.slnx`.

```bash
# API (http://localhost:5134). Applies migrations and seeds demo data on startup.
cd src/ElderSafe.Api && dotnet run

# Dashboard (http://localhost:5173). Proxies /api and /hubs to :5134.
cd src/ElderSafe.Dashboard && npm install && npm run dev

# Backend build and tests
dotnet build ElderSafe.slnx
dotnet test ElderSafe.slnx
dotnet test --filter "Requirement=REQ-FALL-03"                        # one requirement's tests
dotnet test --filter "FullyQualifiedName~EscalationPolicyTests"       # one class
dotnet test --filter "FullyQualifiedName~EscalationPolicyTests.Escalates_to_the_next_caregiver_once_the_timeout_has_passed"

# Backend lint, exactly as CI runs it
dotnet format whitespace ElderSafe.slnx --verify-no-changes --exclude src/ElderSafe.Api/Migrations
dotnet format style ElderSafe.slnx --verify-no-changes --severity error --exclude src/ElderSafe.Api/Migrations

# Dashboard (run from src/ElderSafe.Dashboard)
npm run lint
npm run build          # tsc -b && vite build; writes into ../ElderSafe.Api/wwwroot
npm test               # vitest run
npx vitest run src/lib/escalation.test.ts

# Traceability gate (CI runs this on .trx and JUnit results in artifacts/)
dotnet test ElderSafe.slnx --logger "trx;LogFileName=test-results.trx" --results-directory artifacts
python tools/generate_traceability.py --results artifacts --out artifacts/traceability.md --check

# EF Core migration (Migrations/ is generated code; never hand-edit it)
cd src/ElderSafe.Api && dotnet ef migrations add <Name>
```

Demo accounts are seeded when `DemoData:Enabled` is on. All use the password `Demo-Passw0rd!`:
- `demo.caregiver@example.invalid` (caregiver, first in the escalation order)
- `demo.nurse@example.invalid` (caregiver, second)
- `demo.resident@example.invalid` (resident, configures the others)

## Conventions enforced by CI

- **Every test carries a requirement ID in its display name.** Backend tests use both forms:
  ```csharp
  [Fact(DisplayName = "REQ-FALL-03 escalates ...")]
  [Trait("Requirement", "REQ-FALL-03")]
  ```
  Vitest tests start the `it("REQ-CFG-09 ...")` string with the ID. `tools/generate_traceability.py --check` fails CI in either of two cases:
  - a requirement in `docs/04-software-requirements.md` has no passing test and isn't marked deferred;
  - a test names a requirement that doesn't exist.

  The script parses the display name because `.trx` files drop traits.
- **Braces are mandatory in C#** (`IDE0011` at error severity, enforced at build via `Directory.Build.props`).
- **PR titles must follow Conventional Commits.** `.github/pull_request_template.md` asks for the requirement IDs touched, the tests added, and the risk impact (RISK-IDs in `docs/02-risk-management.md`).
- **`main` is protected.** Changes go through pull requests.

## Architecture

One ASP.NET Core process (`src/ElderSafe.Api`) and one SQLite file. In production the built React dashboard is served from the API's `wwwroot`, so everything is same-origin. That fact underpins the auth design.

### Layering

- **Controllers stay thin.** They read the caller from `ICurrentCaregiver` and call a service. They then map the service's outcome (`null`, a `ConfigurationOutcome` enum or `IngestionOutcome`) to an HTTP status.
- **Domain decisions are pure classes behind interfaces.** `IFallDetector` (`OnnxFallDetector` by default, `ThresholdFallDetector` as a fallback; chosen by `FallDetection:Detector`) and `IEscalationPolicy` (`EscalationPolicy`) take plain inputs and return a verdict or decision with no database access.
  - Their options are passed directly rather than as `IOptions<T>`, so tests can `new` them up.
  - Explicit DI factories in `Program.cs` wire this together. Add new services the same way.
- **Side effects go through ports.**
  - `IEmergencyDispatch`: the only implementation is `LoggingEmergencyDispatch`, and nothing places real calls.
  - `IAlertNotifier`: implemented by `SignalRAlertNotifier`.
- **Time is always injected.** Use `TimeProvider`, never `DateTime.Now`. Tests use `FakeTimeProvider`.
- **Thresholds and timeouts live in `appsettings.json`.** The sections are `FallDetection`, `FallModel`, `Escalation`, `Alerting` (`DetectionWindow`, `EscalationSweepInterval`), `ShiftSummary`, `DemoData` and `Simulation`. The Azure OpenAI endpoint, deployment and key for `ShiftSummary:AzureOpenAI` live in `dotnet user-secrets`, never in a committed file; without them the template writes summaries. `DemoData:Enabled` and `Simulation:Enabled` are `false` in the base file; `appsettings.Development.json` turns them on. With `Simulation` off, the simulate endpoint returns 404.

### Alert pipeline

1. A reading arrives through either the device endpoint `POST /api/readings` or the dashboard's `POST /api/residents/{id}/simulate/{scenario}` (`ScenarioLibrary`). Both go to `ReadingIngestionService`.
2. Ingestion calls `AlertService.EvaluateFallAsync`. This skips evaluation while a fall alert is already open, then runs the detector over the last `DetectionWindow` of readings.
3. On a positive verdict it creates an `Alert` and an `AuditEntry`, saves, and pushes over SignalR.
4. `EscalationWorker` (a `BackgroundService` on a `PeriodicTimer`) calls `AlertService.ApplyEscalationAsync`. For each open alert, the policy decides whether to notify the next caregiver or place an emergency call. A call is placed only when the chain is exhausted and the resident has `EmergencyCallEnabled`, which defaults to off.
5. `DispatchedAt` is re-checked before dispatching, so a call happens at most once. Notifications are sent only after `SaveChanges`.

An alert's state is implied by nullable timestamps (`AcknowledgedAt`, `DispatchedAt`), not stored in a state column. Escalation order is `ResidentCaregiver.Order`, which has a unique index per resident.

### Fall model (ADR-008)

- `OnnxFallDetector` loads `Models/fall/model.onnx` and `model.json` at startup. The API refuses to start if the model's SHA-256 doesn't match `model.json`, or if its feature names differ from `FallFeatures.Names`.
- The features are defined twice: `tools/ml/features.py` for training and `Detection/FallFeatures.cs` for the API. Change both together. The parity test (`OnnxFallDetectorTests`) compares them using `Detection/Fixtures/fall_model_fixture.json`.
- `model.onnx`, `model.json` and the fixture must all come from the same training run. The training code is in `tools/ml`, and the run is an Azure ML job defined in `job.yml`. The [model card](docs/models/fall-model-card.md) has the replacement steps.

### Shift summary (ADR-005)

`Summaries/`: `ShiftSummaryService` grounds the prompt in the shift's stored alerts, under `A1`-style references with no names or IDs. The configured `ISummaryGenerator` writes the text: `AzureOpenAiSummaryGenerator` over plain HTTPS, or `TemplateSummaryGenerator`. `SummaryValidator` then checks it.
- A summary that fails validation is withheld, never shown.
- An unavailable model falls back to the template.
- Change `AzureOpenAiSummaryGenerator.PromptVersion` whenever the prompt changes.
- `ElderSafeWebAppFactory` blanks the API key, so tests never call Azure.

### Auth (ADR-006)

**Two authentication schemes:**
- **People: ASP.NET Core Identity cookie** (`ElderSafe.Auth`).
  - The cookie is `HttpOnly` and `SameSite=Strict`, with an absolute 8-hour expiry.
  - It returns 401/403 instead of redirecting.
  - `UserAccount : IdentityUser<Guid>` has an `AccountType` of `Resident` or `Caregiver`. Registering a `Resident` also creates the account's `Resident` row.
- **Wearables: the custom `Device` scheme** (`Security/DeviceAuthenticationHandler.cs`).
  - The device sends an `X-Device-Key` header, which is SHA-256 hashed and looked up in `Devices.KeyHash`.
  - The resulting principal carries a `resident_id` claim. `ReadingsController` accepts only this scheme.

**Authorization is relationship-based**, with no roles or policies. Every check goes through `IResidentAccess` (`Security/ResidentAccess.cs`):
- `CanViewAsync`: the resident's own account, or an assigned caregiver.
- `CanConfigureAsync`: the resident's own account only.

Use these. Do not write new access queries. The rules that come with this:
- **Answer "not yours" with 404, not 403** (RISK-12).
- **Take the actor from `ICurrentCaregiver`, never from a request body.** The audit log depends on it.
- **SignalR pushes go to group `resident:{id}`.** `AlertsHub.SubscribeToResident` checks `CanViewAsync` before adding a connection to the group.

The dashboard never handles tokens:
- `AuthContext` asks `GET /api/auth/me`, which returns the account plus the residents it may see.
- `RequireAuth` is UX only. All enforcement is on the server.

### Data

- A single `ElderSafeDbContext` derives from `IdentityDbContext<UserAccount, IdentityRole<Guid>, Guid>`.
- `AuditEntries` is **append-only**. `SaveChanges` throws if an audit row is modified or deleted.
- All `DateTimeOffset` values are converted to UTC. Enums are stored as strings in the database and serialized as names in JSON.

### Dashboard

- `src/lib/api.ts` is the only HTTP client: relative paths, `credentials: "include"`.
- `src/lib/signalr.ts` builds the hub connection. Refetch after a reconnect rather than assume nothing was missed (RISK-08).
- Pure decision logic lives in `src/lib/*.ts`, with Vitest tests beside it in the node environment.
- UI components are shadcn/ui, styled with Tailwind v4, using the `@` → `src` path alias.
- **When suggesting or writing a React component, always give it a named TypeScript props interface** (`interface AlertCardProps { ... }`, then `function AlertCard({ ... }: AlertCardProps)`). Don't use inline object types or untyped props. Type-only imports use `import type`, because the project sets `verbatimModuleSyntax`.

## Docs that drift

`docs/05-architecture.md` still describes an earlier design in places and doesn't match the code:

| Docs say | Code has |
|---|---|
| `GeofenceEvaluator` | nothing yet (wandering detection is deferred) |
| `DeviceSimulator` background service | `SimulationController` with `ScenarioLibrary` |
| `ResidentAccessGuard` | `IResidentAccess` |

Trust the code, and update the docs when touching those areas. Deferred features (wandering, the "no data" alert, read auditing) are listed in `README.md` and marked deferred in the requirements register. Implementing one means writing tests tagged with its REQ IDs and removing the deferred marker.
