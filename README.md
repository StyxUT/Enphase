# EnphaseLocal

A .NET 10 minimal API that acts as a local gateway to an [Enphase Envoy](https://enphase.com/en-us/support/envoy) solar inverter system. Fetches live production and consumption data from the Envoy's JSON endpoint and exposes it via HTTP for dashboards, automation, or local monitoring.

## Features

- **Live solar dashboard** — Renders an auto-refreshing HTML page with net power, production, and consumption tiles
- **Gradient visualization** — Production shown with a gray-to-green gradient (0–2000+ W); consumption with gray-to-red (≤2000–4000+ W); net power colored red/yellow/green
- **REST API** — JSON endpoints for production and consumption data
- **Health check** — Lightweight endpoint for container orchestration
- **Polly retry policy** — 5 retries with exponential backoff (3^retry seconds) for transient HTTP failures
- **Swagger UI** — Enabled in all environments at `/swagger`
- **Docker support** — Multi-stage Dockerfile and CI-published image

## Architecture

```
┌─────────────┐     HTTP (Bearer)     ┌──────────────┐
│  External   │ ◄───────────────────  │  Enphase     │
│  Client     │     GET /production   │  Envoy       │
│  (browser,  │     /consumption      │  (local net) │
│   curl,     │     /netpowerproduct  │              │
│   k8s probe)│     /healthcheck      │              │
└─────────────┘                       └──────────────┘
       ▲
       │
       │  .NET 10 Minimal API
       │  ┌─────────────────────┐
       │  │  Program.cs         │
       │  │  EnphaseService     │── HttpClient
       │  │  EnphaseOptions     │── IOptionsMonitor
       │  │  Polly retry        │── HttpPolicyExtensions
       │  └─────────────────────┘
```

The API uses a **minimal API** pattern (no `Startup` class). Configuration is loaded from `appsettings.json`, environment-specific overrides, environment variables, and User Secrets. HTTP resilience is handled by Polly with a policy on the typed `HttpClient`.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An Enphase Envoy on your local network with its local API enabled
- (Optional) Docker for containerized deployment

## Configuration

All Enphase-specific settings live under the `"Enphase"` config section:

| Key | Description | Default | Required |
|-----|-------------|---------|----------|
| `Enphase:BaseAddress` | Base URL of the Envoy (e.g. `https://envoy.home`) | `https://envoy.home` | Yes |
| `Enphase:BearerToken` | Authentication token for the Envoy API | — | Yes (prod) |

### appsettings.json (shared defaults)

```json
{
  "Enphase": {
    "BaseAddress": "https://envoy.home"
  }
}
```

### appsettings.Production.json (production secrets)

```json
{
  "Enphase": {
    "BearerToken": "your-bearer-token-here",
    "BaseAddress": "https://envoy.home"
  }
}
```

The bearer token can also be supplied via environment variable `Enphase__BearerToken` or User Secrets during development.

## Running

### Local development

```bash
dotnet run --project EnphaseLocal
# Launches at http://localhost:5280 with Swagger at /swagger
```

### With a specific environment

```bash
dotnet run --project EnphaseLocal --environment Production
# HTTPS redirection disabled; Swagger still available
```

### Docker

```bash
docker build -t enphase-local .
docker run -p 8080:80 \
  -e Enphase__BaseAddress=https://envoy.home \
  -e Enphase__BearerToken=your-token \
  enphase-local
```

### Kubernetes (K3s)

Kubernetes manifests for deploying this application on K3s are available at [github.com/styxut/k3s-Enphase](https://github.com/styxut/k3s-Enphase). The manifests include a Deployment, Service, and Kustomize overlay — deploy with:

```bash
kubectl apply -k https://github.com/styxut/k3s-Enphase
```

## API Reference

| Endpoint | Method | Response |
|----------|--------|----------|
| `/netpowerproduction` | GET | HTML dashboard (auto-refresh every 60 s) |
| `/healthcheck` | GET | `204 No Content` |
| `/production` | GET | JSON array of production meters |
| `/consumption` | GET | JSON array of consumption meters |

All JSON endpoints return `application/json`. On upstream Envoy failure (`HttpRequestException`), they return `502 Bad Gateway` with `application/problem+json`.

### `/netpowerproduction`

Renders an HTML page with three tiles:

1. **Net Power Production** — large centered value; gradient: green (≥250 W) → yellow (0–250 W) → red (<0 W)
2. **Current Power Production** — gray→green gradient (0 W → ≥2000 W)
3. **Power Consumption** — gray→red gradient (≤2000 W → ≥4000 W)

The page auto-refreshes every 60 seconds via `<meta http-equiv="refresh">`.

## Project Structure

```
EnphaseLocal/
├── EnphaseLocal.csproj          # Web API project (net10.0)
├── Program.cs                   # Minimal API entry point, endpoints, helpers
├── EnphaseOptions.cs            # Strongly-typed configuration POCO
├── Models/DTO/
│   └── ProductionDataDto.cs     # DTO records matching Envoy JSON schema
├── Services/
│   ├── IEnphaseService.cs       # Service interface
│   └── EnphaseService.cs        # Fetches & caches data from /production.json
├── Views/
│   └── NetPowerProduction.html  # HTML template with placeholder tokens
├── Properties/
│   └── launchSettings.json
├── Dockerfile
└── appsettings*.json

Tests/
└── EnphaseLocal.Tests/
    ├── EnphaseLocal.Tests.csproj
    ├── EnphaseLocalApplicationFactory.cs  # WebApplicationFactory<Program>
    ├── EnphaseLocalTests.cs               # Service-level unit tests
    ├── BasicEndpointsTests.cs             # Integration tests for endpoints
    └── NetPowerProductionEndpointTests.cs # HTML template integration test
```

## Development

### Build

```bash
dotnet build Enphase.sln
```

### Test

```bash
dotnet test Tests/EnphaseLocal.Tests/EnphaseLocal.Tests.csproj
```

```bash
# Single test by filter
dotnet test Tests/EnphaseLocal.Tests/EnphaseLocal.Tests.csproj \
  --filter FullyQualifiedName~EnphaseLocalTests.GetNetPowerProductionAsync_ReturnsExpectedValue
```

```bash
# With coverage
dotnet test --collect:"XPlat Code Coverage"
```

### CI/CD

Two GitHub Actions workflows are configured:

- **PR tests** (`.github/workflows/pr-tests.yml`) — runs on pull requests to `master`; builds and runs the test suite
- **Docker publish** (`.github/workflows/docker-publish.yml`) — on push to `master`, builds, tests, and publishes a Docker image to `styxut/enphase-local:latest` on Docker Hub

## Security Notes

- The HTTP client is configured with `DangerousAcceptAnyServerCertificateValidator` because many Envoy units use self-signed TLS certificates. In production, ensure the network is trusted.
- Authentication to the Envoy uses a Bearer token. Store secrets via User Secrets for development or environment variables in production — never commit tokens to the repository.
- HTTPS redirection is enabled in non-Production environments only.

## Domain Notes

- **Enphase Envoy** — The local gateway device installed with Enphase solar systems. Exposes a local API at `/production.json` with JSON data for production, consumption, and storage meters.
- **EIM** (Enphase Intelligence Meter) — The consumption meter type. The service prefers `eim`-type production data when calculating net power.
- **Net Power** — `production WNow - consumption WNow`. Negative values indicate the home is consuming more than the solar array is producing.

## License

MIT
