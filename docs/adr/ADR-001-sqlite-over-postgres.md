# ADR-001: Use SQLite instead of PostgreSQL

**Status:** Accepted
**Date:** 2026-11-05

## Context

ElderSafe stores residents, caregivers, readings, alerts and an audit log. The original plan named PostgreSQL, which is the right choice for a real deployment: concurrent writers, durability, and the operational tooling a care provider would expect.

The build budget for this project is one to two days. PostgreSQL costs a Docker Compose file, a container that must be running before anything works, connection strings in two environments, and a managed Azure instance to deploy against.

A reviewer who cannot run the project in under a minute will not run it at all.

## Options

1. **PostgreSQL in Docker** — production-realistic, but adds setup before the first line of domain code and a second thing that can be broken on a reviewer's machine.
2. **SQLite file through EF Core** — no server, no container, the database is a file. EF Core makes the provider a one-line change later.
3. **In-memory only** — fastest to write, but the audit log and alert history lose their meaning if they vanish on restart, and REQ-SEC-02 is about an append-only record.

## Decision

Use SQLite through EF Core, with migrations checked in.

The data access goes through EF Core and `DbContext` rather than raw SQL, so the provider can be swapped without touching application code.

## Consequences

**Good**

- `dotnet run` is the whole setup. No container, no connection string to configure.
- Azure deployment needs no database resource, which keeps the demo free to host.
- Migrations and the EF Core model are real, so the schema design is still on display.

**Bad**

- SQLite allows one writer at a time. Acceptable for one process and a simulated device; wrong for real concurrent load.
- Some PostgreSQL features cannot be demonstrated: concurrent write behaviour, JSONB, geospatial types through PostGIS. The geofence is therefore plain arithmetic rather than a spatial query.
- The file lives on the Azure Web App's filesystem, which is not durable across restarts or scale-out. The demo treats data as disposable.

**Noted in the architecture document** under "What this design gives up", so the limitation is visible rather than buried here.

## Revisit when

The project needs more than one writer, durable storage, or spatial queries.
