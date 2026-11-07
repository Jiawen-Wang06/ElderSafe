# ADR-004: Deploy as a single Azure Web App, without Bicep on day one

**Status:** Accepted
**Date:** 2026-11-05

## Context

The project needs a live URL. A reviewer who can click a link and watch an alert appear learns more in thirty seconds than from any amount of README prose.

The original plan had Bicep templates building an App Service plan, a staging slot, PostgreSQL Flexible Server, Key Vault, Application Insights and Azure OpenAI, deployed through a gated pipeline. That is the right shape for a regulated product and roughly a day of work by itself.

## Options

1. **Full Bicep environment with slots and a gated pipeline** — demonstrates infrastructure as code properly, but consumes most of the build budget and costs money to keep running.
2. **Single Web App, deployed with `az webapp up`** — a live URL within minutes, free tier, nothing else to provision.
3. **No deployment, screenshots only** — cheapest, and it loses the most valuable thing the project has.

## Decision

Deploy a single Azure Web App on the free tier. The React dashboard is built into the API's `wwwroot` and served by the same app, so there is one deployable artifact.

The first deployment was done manually: `dotnet publish` and `npm run build` run locally, zipped, and pushed through the Kudu Zip Deploy endpoint with Basic Authentication enabled on the Web App. A GitHub Actions job using OpenID Connect, so no Azure credential is stored as a secret, is the intended path once redeploys are frequent enough to be worth automating — see Revisit when.

Infrastructure as code is deferred, not abandoned. It is listed as the next piece of work once the application exists.

## Consequences

**Good**

- A live demo link in the README.
- Free tier, so it can stay up during a job search without cost.
- One artifact to build and deploy, matching the one-process design in ADR-003.

**Bad**

- No staging slot, so there is no smoke test against a deployed environment before production. The CI/CD diagram in the roadmap shows a gate this deployment does not yet have.
- No Key Vault. There are no real secrets yet, and when the LLM feature arrives one will be needed.
- The free tier sleeps when idle, so the first request after a quiet period is slow. The README warns a reviewer.
- SQLite on the Web App filesystem is not durable across restarts (see ADR-001).
- Basic Authentication is enabled on the Web App for Kudu's deployment endpoint, and the deployment credential is a username and password typed by hand rather than an OIDC federated identity. Fine for a manual, infrequent deploy; the next deployment-rigor pass (see Revisit when) should move this to the GitHub Actions OIDC path originally planned and disable Basic Authentication afterward.

## Revisit when

The application is complete enough that deployment rigor is the weakest part of the project. At that point a GitHub Actions job using OpenID Connect replaces the manual zip deploy, Basic Authentication is disabled, and Bicep, a staging slot and a production approval gate become the next piece of work — a good standalone pull request.
