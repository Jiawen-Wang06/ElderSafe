# ADR-009: Deploy as a container image from GitHub Container Registry

**Status:** Accepted. Amends [ADR-004](ADR-004-single-web-app.md).
**Date:** 2026-11-19

## Context

ADR-004 deployed the app by building it on a laptop, zipping it, and pushing the zip to a Windows Web App through Kudu with Basic Authentication. Every release therefore depended on one machine's toolchain and on a hand-typed deployment password.

The application now has more moving parts than a zip shows at a glance:
- a native ONNX Runtime library for the fall model ([ADR-008](ADR-008-trained-fall-model.md));
- a model file whose hash is checked at startup;
- a dashboard that must be built into the API's `wwwroot`;
- an optional language-model configuration ([ADR-005](ADR-005-grounded-llm-summary.md)).

A release should be built the same way every time, and tested as the thing that will run.

## Options

1. **Keep the manual zip deploy.** It works, but each release depends on a laptop, and Basic Authentication stays on.
2. **Zip deploy from GitHub Actions with OpenID Connect.** This is ADR-004's planned next step. It is reproducible, but what runs in Azure is still assembled by Azure's runtime image, not by the build.
3. **A container image, built and tested in GitHub Actions and pulled by a Linux Web App.** The image that passed the smoke test is the image that runs.

For the registry:
- **GitHub Container Registry (GHCR):** free for a public repository, and pushed with the workflow's own token.
- **Azure Container Registry:** about $5 a month, and needs a federated identity and role assignments before the first push.

## Decision

Option 3, with GHCR.

- **One image.** A multi-stage `Dockerfile` builds the dashboard with Node 22 and publishes the API for `linux-x64` with the .NET 10 SDK. It runs on `mcr.microsoft.com/dotnet/aspnet:10.0` as the image's non-root `app` user, on port 8080.
- **Built and tested in CI.** `.github/workflows/docker.yml` builds the image on every pull request and smoke-tests it: it starts the container, checks the API answers and the dashboard is served, signs in, and raises a simulated fall. That last step runs the ONNX model, so a missing model or native library fails the PR. On `main`, the same tested image is pushed as `ghcr.io/jiawen-wang06/eldersafe:latest` and `:sha-<commit>`.
- **Released by pulling.** A Linux Web App runs `:latest`. A release is a restart, or a pull through the Web App's continuous-deployment webhook. A known-good build can be pinned by its `sha-` tag.
- **Configuration through App Service settings,** never baked into the image. These include the demo switches and the Azure OpenAI endpoint, deployment and key.
- **The database stays inside the container.** It resets when the container is replaced, exactly as ADR-001 already accepted for the zip deploy. The demo seeder recreates the demo accounts on every start. The `/home` storage share is deliberately not used for SQLite, because SQLite's locking is unreliable on network file systems.

## Consequences

**Good**

- Every release is built the same way, and the image that runs is the one that passed the smoke test.
- No Azure credential is stored in GitHub, and Kudu Basic Authentication is no longer needed.
- `sha-` tags give an exact rollback.
- The same image runs locally with `docker run`, which makes "works on my machine" a smaller claim.

**Bad**

- **A Linux Web App is needed.** The existing Windows app cannot run Linux containers, so this is a second app with a new URL.
- **The free tier may not be enough.** It is unclear whether F1 supports custom containers, and its 60 CPU minutes a day are tight for an always-on escalation worker. B1 (about $13 a month, covered by trial credit) is the dependable choice.
- **Sessions do not survive a restart.** ASP.NET Core's data-protection keys live in the container, so a restart signs everyone out. That is acceptable for a demo, and not for a product.
- **Data still resets on restart,** as before.
- **`:latest` makes the running version less obvious.** The model version in every fall verdict, and the commit in the `sha-` tag, are the record of what ran.
- **The image's base layers are third-party software** that this project does not patch. They are rebuilt only when a new image is built. See the [SOUP list](../06-soup.md).

## Revisit when

- The demo needs data that survives a restart. A managed database then replaces SQLite (ADR-001).
- Releases need a gate. That means a staging slot, a smoke test against it, then a swap, which ADR-004 already lists as the next step.
- Infrastructure needs to be reproducible. Bicep would then describe the Web App and its settings, instead of portal clicks.
