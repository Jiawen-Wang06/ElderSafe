# syntax=docker/dockerfile:1
#
# ElderSafe as one container: the React dashboard is built into the API's
# wwwroot and served by the same process, as on the original Web App
# (ADR-004, ADR-009).
#
# Build from the repository root:
#   docker build -t eldersafe .
#   docker run -p 8080:8080 -e DemoData__Enabled=true -e Simulation__Enabled=true eldersafe

# ---- 1. Dashboard ---------------------------------------------------------
FROM node:22-bookworm-slim AS dashboard
WORKDIR /src/src/ElderSafe.Dashboard

# Dependencies first, so editing the dashboard does not re-run npm ci.
COPY src/ElderSafe.Dashboard/package.json src/ElderSafe.Dashboard/package-lock.json ./
RUN npm ci

# vite.config.ts writes the build into ../ElderSafe.Api/wwwroot.
COPY src/ElderSafe.Dashboard/ ./
RUN npm run build

# ---- 2. API ----------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src

# Repository-wide build settings: the code-style analyzers run at build time.
COPY Directory.Build.props .editorconfig ./

# Restore first, for the same layer-caching reason as npm ci above. Linux
# x64 only, so the image carries ONNX Runtime's Linux library and not the
# Windows and macOS ones.
COPY src/ElderSafe.Api/ElderSafe.Api.csproj src/ElderSafe.Api/
RUN dotnet restore src/ElderSafe.Api/ElderSafe.Api.csproj -r linux-x64

COPY src/ElderSafe.Api/ src/ElderSafe.Api/
COPY --from=dashboard /src/src/ElderSafe.Api/wwwroot src/ElderSafe.Api/wwwroot
RUN dotnet publish src/ElderSafe.Api/ElderSafe.Api.csproj \
    -c Release -r linux-x64 --self-contained false --no-restore -o /app

# ---- 3. Runtime ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# The image runs as the non-root "app" user, which cannot write to /app.
# SQLite gets its own writable directory instead. It lives inside the
# container, so data resets whenever the container is replaced or restarted;
# the demo seeder recreates the demo accounts on every start (ADR-001,
# ADR-009).
RUN mkdir -p /data && chown "$APP_UID" /data

COPY --from=api /app ./

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__ElderSafe="Data Source=/data/eldersafe.db"

EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ElderSafe.Api.dll"]
