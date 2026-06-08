# Claims Intelligence — CLAUDE.md

This file gives Claude Code the full context needed to work in this repo without re-deriving it from scratch each session. Read this before starting any task.

## What this project is

A .NET 9 + Next.js 15 port of a Python/React proof-of-concept that processes multi-document insurance claims using Azure AI services. It extracts structured data from uploaded documents, runs confidence scoring, generates AI-powered summaries, and performs gap/discrepancy analysis across the claim.

The source project (Python) lives at `../claimsintelligence` and will be deleted once this port is complete. Do not modify files in `../claimsintelligence`.

## Repo layout

```
claims-intelligence/
  ClaimsIntelligence.sln                   ← single solution, all backend projects
  CLAUDE.md                                ← this file
  PLAN.md                                  ← task checklist — update as work progresses
  .gitignore
  .devcontainer/                           ← .NET 9 + Node 22 + Azure CLI dev container
  backend/src/
    ClaimsIntelligence.Domain/             ← shared models, interfaces, enums (no Azure deps)
    ClaimsIntelligence.Infrastructure/     ← Azure SDK wrappers (Blob, Queue, Cosmos, OpenAI, CU)
    ClaimsIntelligence.Api/                ← ASP.NET Core Web API gateway (50+ endpoints)
    ClaimsIntelligence.ContentProcessor/   ← .NET Worker Service (4-stage extraction pipeline)
    ClaimsIntelligence.Workflow/           ← .NET Worker Service (DAG orchestrator, 4 executors)
    ClaimsIntelligence.Tests/              ← xUnit tests for all backend projects
  frontend/src/                            ← Next.js 15 App Router (claims journey UI)
  infra/                                   ← Azure Bicep templates (ported from source)
  docs/                                    ← Architecture and deployment documentation
```

## Architecture

### Two-level processing

**Claim level** — `ClaimsIntelligence.Workflow`
- Listens on `claim-process-queue` (Azure Storage Queue)
- Runs a DAG workflow with 4 executor stages in sequence:
  1. `DocumentProcessExecutor` — calls API to trigger per-document extraction, polls for completion
  2. `RaiExecutor` — responsible-AI safety gate (10 categories) via Azure OpenAI
  3. `SummarizeExecutor` — cross-document AI summary via Azure OpenAI
  4. `GapExecutor` — gap/discrepancy analysis using a YAML DSL ruleset + Azure OpenAI
- Persists checkpoint state to Cosmos DB for fault tolerance
- Polly retry with exponential backoff + jitter; dead-letter queue for failed messages

**Document level** — `ClaimsIntelligence.ContentProcessor`
- Listens on `content-pipeline-extract-queue` (Azure Storage Queue)
- 4-stage pipeline per document:
  1. **Extract** — Azure AI Content Understanding (prebuilt-layout + linked analyzers)
  2. **Map** — schema field extraction from CU output
  3. **Evaluate** — merge + dual confidence scoring (OCR-level + OpenAI log-probability)
  4. **Save** — persist results to Blob Storage + Cosmos DB
- Dynamic handler loading from App Configuration
- OpenTelemetry spans on each stage; exported to Azure Monitor

**API gateway** — `ClaimsIntelligence.Api`
- ASP.NET Core Web API
- Endpoint groups: `/contentprocessor`, `/claimprocessor`, `/schemavault`, `/schemasetvault`, `/claimsdemo`
- Managed Identity auth — no connection strings or secrets in code
- MongoDB.Driver for Cosmos DB (Mongo API)
- Swagger/OpenAPI via Swashbuckle

**Frontend** — `frontend/src`
- Next.js 15 App Router, React 19, TypeScript
- Fluent UI v9 (`@fluentui/react-components`)
- MSAL for Azure AD auth (`@azure/msal-react`)
- Zustand state management
- 7-step claim journey: Documents → Entities → Coverage → Fraud → Review → Recommendation → Email
- `next.config.js` proxies `/api` to the backend in dev (avoids CORS)

## Azure services

| Service | Used by |
|---|---|
| Azure Storage Queues | ContentProcessor (extract queue), Workflow (claim queue + DLQ) |
| Azure Blob Storage | ContentProcessor (save results), API (read/write docs) |
| Azure Cosmos DB (Mongo API) | All backend services — `processes`, `claimprocesses`, `schemas` collections |
| Azure AI Content Understanding | ContentProcessor extract stage |
| Azure OpenAI (GPT-5.1) | Workflow — RAI gate, summarize, gap analysis |
| Azure App Configuration | All services — settings, feature flags (no secrets) |
| Azure Container Apps | Runtime for all 3 backend services + frontend |
| Azure Container Registry | Container image storage |
| Azure AI Foundry | AI project hub |

## Python → .NET decisions

| Python | .NET equivalent |
|---|---|
| FastAPI | ASP.NET Core Minimal API endpoint groups |
| Pydantic models | C# `record` types + FluentValidation |
| PyMongo | MongoDB.Driver |
| `azure-identity` | `Azure.Identity` (`DefaultAzureCredential`) |
| `azure-storage-blob` | `Azure.Storage.Blobs` |
| `azure-storage-queue` | `Azure.Storage.Queues` |
| `azure-appconfiguration` | `Azure.Data.AppConfiguration` |
| `azure-ai-inference` (OpenAI) | `Azure.AI.OpenAI` |
| agent-framework DAG engine | Custom `IExecutor` + `WorkflowBuilder` in C# |
| `tenacity` retry | Polly |
| YAML DSL gap rules | YamlDotNet (same `.yaml` format, ported as-is) |
| OpenTelemetry Python SDK | OpenTelemetry .NET + Azure Monitor exporter |
| pytest + pytest-asyncio | xUnit + Moq + FluentAssertions |

## Design rules

- **No secrets in code.** All Azure clients use `DefaultAzureCredential`. Config values from Azure App Configuration or environment variables only.
- **Single solution, separate executables.** `ClaimsIntelligence.sln` builds everything; each backend project produces its own container image and runs/deploys independently.
- **Domain has zero Azure dependencies.** `ClaimsIntelligence.Domain` is pure C# — models, interfaces, enums. All Azure SDK usage lives in `ClaimsIntelligence.Infrastructure`.
- **DI throughout.** All services registered via `IServiceCollection` extension methods. No service locator pattern.
- **Polly for all retries.** Exponential backoff with jitter on queue polling and HTTP calls. Max 5 retries before dead-letter.
- **OpenTelemetry tracing on pipeline steps.** Each of the 4 extraction stages emits spans; exported to Azure Monitor via `azure-monitor-opentelemetry` exporter.
- **Non-root containers.** All Dockerfiles run as a non-root user.

## Running locally (dev container)

```bash
# backend — API
dotnet run --project backend/src/ClaimsIntelligence.Api

# backend — ContentProcessor worker
dotnet run --project backend/src/ClaimsIntelligence.ContentProcessor

# backend — Workflow worker
dotnet run --project backend/src/ClaimsIntelligence.Workflow

# frontend
cd frontend/src && npm run dev        # http://localhost:3000

# all services via docker-compose
docker-compose up
```

Copy `.env.example` → `.env.local` and fill in your Azure resource values before running.

## Source reference

When porting, the Python source lives at `../claimsintelligence/src/`:
- `ContentProcessor/` — queue worker + 4-stage pipeline
- `ContentProcessorAPI/` — FastAPI gateway (routers, models, utils)
- `ContentProcessorWorkflow/` — agent framework DAG + 4 executors + YAML DSL rules
- `ContentProcessorClaimsDemo/` — React 19/Vite SPA (7-step journey)

## Current status

See `PLAN.md` for the live task checklist.
