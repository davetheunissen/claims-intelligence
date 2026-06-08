# Claims Intelligence — Port Plan

Porting `../claimsintelligence` (Python/React) to .NET 9 + Next.js 15.
Check off tasks as they complete. Dependencies are noted inline.

---

## Milestone 0 — Scaffold & repo setup

- [x] **#1** Scaffold folder structure, `.gitignore`, `.sln`, copy `docs/` and `infra/`, create `CLAUDE.md` and `PLAN.md`
- [x] **#10** Create private GitHub repo and push — https://github.com/davetheunissen/claims-intelligence

---

## Milestone 1 — Shared backend foundation

- [x] **#2** `ClaimsIntelligence.Domain` — shared C# models, interfaces, enums *(unblocked after #1)*
- [x] **#3** `ClaimsIntelligence.Infrastructure` — Azure SDK wrappers (Blob, Queue, Cosmos, OpenAI, CU, AppConfig) *(unblocked after #2)*

---

## Milestone 2 — Backend services *(all unblock after #3, can run in parallel)*

- [x] **#4** `ClaimsIntelligence.ContentProcessor` — .NET Worker Service, 4-stage extract pipeline
- [x] **#5** `ClaimsIntelligence.Api` — ASP.NET Core Web API, 5 endpoint groups, 50+ endpoints
- [x] **#6** `ClaimsIntelligence.Workflow` — .NET Worker Service, DAG engine, 4 executors, YAML DSL gap rules

---

## Milestone 3 — Tests & frontend *(unblocked after their respective deps)*

- [ ] **#7** `ClaimsIntelligence.Tests` — xUnit, port all 3 Python test suites *(unblocked after #4, #5, #6)*
- [x] **#8** Next.js 15 frontend — 7-step journey, MSAL auth, Fluent UI *(unblocked after #1)*

---

## Milestone 4 — Dev container & infra

- [x] **#9** Dev container — `.devcontainer/`, `.env.example`, `docker-compose.yml` *(unblocked after #1)*

---

## Task detail summary

| # | Task | Blocked by | Notes |
|---|------|------------|-------|
| 1 | Scaffold repo structure | — | Creates folder layout, .sln stub, copies docs/infra |
| 2 | Domain project | 1 | Pure C#, no Azure deps |
| 3 | Infrastructure project | 2 | All Azure SDK wrappers |
| 4 | ContentProcessor worker | 3 | BackgroundService + 4-stage pipeline |
| 5 | API project | 3 | Minimal API endpoint groups, Swashbuckle |
| 6 | Workflow worker | 3 | DAG engine, Polly retries, YAML DSL |
| 7 | Tests project | 4,5,6 | xUnit + Moq + FluentAssertions |
| 8 | Next.js frontend | 1 | App Router, Fluent UI v9, MSAL |
| 9 | Dev container | 1 | .NET 9 + Node 22 + Azure CLI |
| 10 | GitHub repo + push | 1 | Private repo via `gh repo create` |

---

## Session resume instructions

When resuming in a new session:
1. Read `CLAUDE.md` for full architecture context
2. Check this file for which tasks are checked off
3. Pick up the next unchecked task — tasks with all deps checked are ready to start
4. The source Python code is at `../claimsintelligence/src/` for reference when porting
