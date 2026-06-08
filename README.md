# Claims Intelligence

A .NET 9 + Next.js 15 solution that processes multi-document insurance claims using Azure AI
services. It extracts structured data from uploaded documents, runs dual confidence scoring,
generates AI summaries, and performs gap/discrepancy analysis across a claim.

This README covers two things:

1. **[Provision the Azure infrastructure](#1-provision-the-azure-infrastructure)** the services depend on.
2. **[Run the backend and frontend in a dev container](#2-run-in-a-dev-container)** (locally or in Codespaces).

For deeper architecture context see [`CLAUDE.md`](./CLAUDE.md); for the full cloud deployment
(Container Apps, app registrations, schema seeding) see [`docs/DeploymentGuide.md`](./docs/DeploymentGuide.md).

---

## Architecture at a glance

| Component | Project | Role |
|---|---|---|
| API gateway | `backend/src/ClaimsIntelligence.Api` | ASP.NET Core Web API, 5 endpoint groups |
| Document pipeline | `backend/src/ClaimsIntelligence.ContentProcessor` | Worker — Extract → Map → Evaluate → Save |
| Claim orchestrator | `backend/src/ClaimsIntelligence.Workflow` | Worker — DAG: DocumentProcess → RAI → Summarize → Gap |
| Frontend | `frontend/src` | Next.js 15 App Router, 7-step claim journey |

All backend services authenticate to Azure via `DefaultAzureCredential` — **no secrets in code**.
Configuration comes from environment variables (`.env.local`) and Azure App Configuration.

---

## 1. Provision the Azure infrastructure

The services need the following Azure resources. You can provision them all at once with the
included Bicep templates, or point the app at resources you already have.

| Service | Used for |
|---|---|
| Azure Storage (Blob + Queues) | Document storage, extract queue, claim queue + DLQ |
| Azure Cosmos DB (NoSQL API) | `processes`, `claimprocesses`, `schemas`, `schemasets` containers |
| Azure AI Content Understanding | Document extraction (Extract stage) |
| Azure AI Foundry / Azure OpenAI | RAI gate, summarize, gap analysis |
| Azure App Configuration | Runtime settings + feature flags (no secrets) |
| Azure Application Insights | OpenTelemetry traces/metrics |
| Azure Container Registry + Container Apps | Image hosting + runtime (cloud deploy only) |

### Prerequisites

- An Azure subscription with **Contributor** + **User Access Administrator** (role assignments are created).
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) (`az`) — `az login` first.
- [Azure Developer CLI](https://aka.ms/install-azd) (`azd`) for the one-command path.
- A region with GPT and Content Understanding capacity. Supported regions: `australiaeast`,
  `centralus`, `eastasia`, `eastus2`, `japaneast`, `northeurope`, `southeastasia`, `uksouth`.

### Option A — Full cloud deployment (`azd up`)

This is the end-to-end path: it provisions every resource, builds the four service images into a
per-deploy Container Registry, deploys them to Container Apps, registers the sample schemas, and
seeds the AI Search indexes. It takes ~25–40 minutes.

```bash
azd auth login
azd up      # prompts for environment name, subscription, region
```

Follow [`docs/DeploymentGuide.md`](./docs/DeploymentGuide.md) for the complete walkthrough,
including the two Microsoft Entra app registrations (`APP_WEB_CLIENT_ID`, `APP_API_SCOPE`) that
must be set with `azd env set` before `azd up`.

> The Bicep parameter file (`infra/main.parameters.json`) uses `azd` environment substitution, so
> `azd` is the supported driver for the full template. Cost/posture toggles
> (`enablePrivateNetworking`, `enableScalability`, `enableRedundancy`, `deployJumpbox`) live in that
> file — see [`docs/CostProfiles.md`](./docs/CostProfiles.md).

### Option B — Provision resources only, run the app locally

If you just want the Azure backing services (Storage, Cosmos, Content Understanding, OpenAI,
App Configuration, App Insights) and intend to run the .NET services and frontend yourself in a
dev container, provision the resource group with Bicep and then capture the endpoints into
`.env.local` (next section).

```bash
# create the resource group
az group create -n rg-claims-dev -l australiaeast

# deploy the infrastructure template
az deployment group create \
  -g rg-claims-dev \
  -f infra/main.bicep \
  -p infra/main.parameters.json \
  -p solutionName=claimsdev location=australiaeast
```

After provisioning, grant your own identity data-plane access so `DefaultAzureCredential` works
from your machine/dev container — e.g. **Storage Blob Data Contributor**, **Storage Queue Data
Contributor**, and **Cosmos DB Built-in Data Contributor** on the respective accounts.

---

## 2. Run in a dev container

The repo ships a dev container (`.devcontainer/devcontainer.json`) with .NET 9, Node 22, and the
Azure CLI preinstalled. This is the recommended way to run the stack — no local SDK installs.

### Prerequisites

- **[Docker Desktop](https://www.docker.com/products/docker-desktop/)** running, **plus**
  [VS Code](https://code.visualstudio.com/) with the
  [Dev Containers extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode-remote.remote-containers) —
  **or** open the repo in [GitHub Codespaces](https://github.com/features/codespaces) (no local Docker needed).

### Step 1 — Open the dev container

- **VS Code:** open the repo folder → command palette → **Dev Containers: Reopen in Container**.
- **Codespaces:** **Code ▸ Create codespace on main** from GitHub.

On first start, `postCreateCommand` runs `dotnet restore` for the backend and `npm install` for the
frontend automatically. Ports **8080** (API), **8081** (ContentProcessor), **8082** (Workflow), and
**3000** (Frontend) are forwarded.

### Step 2 — Configure environment variables

Copy the template and fill in the endpoints from the resources you provisioned in Part 1:

```bash
cp .env.example .env.local
```

`.env.local` is git-ignored. Each backend service authenticates with `DefaultAzureCredential`, so
inside the container run:

```bash
az login        # or: az login --use-device-code  (Codespaces / VS Code Web)
```

You can pull most endpoint values straight from Azure, for example:

```bash
az cosmosdb show -g rg-claims-dev -n <cosmos-account> --query documentEndpoint -o tsv
az storage account show -g rg-claims-dev -n <storage-account> --query primaryEndpoints.blob -o tsv
```

Fill these into `.env.local` (see `.env.example` for the full annotated list):

| Variable | Value |
|---|---|
| `AZURE__BlobStorageAccountUrl` | `https://<storage>.blob.core.windows.net` |
| `AZURE__QueueStorageAccountUrl` | `https://<storage>.queue.core.windows.net` |
| `AZURE__CosmosEndpoint` | `https://<cosmos>.documents.azure.com:443/` |
| `AZURE__ContentUnderstanding__Endpoint` | `https://<cu>.cognitiveservices.azure.com` |
| `AZURE__AzureInferenceEndpoint` | Foundry inference endpoint |
| `AZURE__AppConfigurationEndpoint` | `https://<appconfig>.azconfig.io` |
| `AZURE__ApplicationInsightsConnectionString` | App Insights connection string |

### Step 3 — Run the services

Open a terminal per service inside the container.

```bash
# Build everything once
cd backend && dotnet build

# API gateway → http://localhost:8080  (Swagger at /swagger)
ASPNETCORE_URLS=http://+:8080 dotnet run --project src/ClaimsIntelligence.Api

# Document pipeline worker
dotnet run --project src/ClaimsIntelligence.ContentProcessor

# Claim orchestrator worker
dotnet run --project src/ClaimsIntelligence.Workflow
```

```bash
# Frontend → http://localhost:3000
cd frontend/src && npm run dev
```

> Bind the API to port **8080** (`ASPNETCORE_URLS=http://+:8080`) so the frontend's dev proxy finds
> it. `next.config.ts` rewrites `/api/*` to `NEXT_PUBLIC_API_URL` (default `http://localhost:8080`),
> which avoids CORS in development.

Open **http://localhost:3000** and work through the 7-step claim journey:
Documents → Entities → Coverage → Fraud → Review → Recommendation → Email.

### Step 4 (alternative) — Run the whole stack with Docker Compose

To build and run all four services as containers together (the worker pipeline end-to-end):

```bash
docker-compose up --build
```

This uses each project's `Dockerfile`, reads `.env.local`, waits for the API health check, then
starts the workers and frontend on the shared `claims-net` network. The frontend is wired to the
API via `NEXT_PUBLIC_API_URL=http://api:8080`.

---

## Running the tests

```bash
cd backend && dotnet test
```

`ClaimsIntelligence.Tests` (xUnit + Moq + FluentAssertions) covers all three backend projects.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| `DefaultAzureCredential` 401/403 | Run `az login`; ensure your identity has the data-plane RBAC roles from Part 1. |
| Frontend `/api/*` calls 404/refused | Make sure the API is running on **8080** (`ASPNETCORE_URLS=http://+:8080`). |
| Codespaces `az login` hangs | Use `az login --use-device-code`. |
| Cloud deploy auth errors (`AADSTS90013` / `AADSTS50011`) | App registration not set — see [`docs/DeploymentGuide.md`](./docs/DeploymentGuide.md) §3.1. |

More: [`docs/TroubleShootingSteps.md`](./docs/TroubleShootingSteps.md).
