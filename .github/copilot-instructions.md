# Copilot Instructions

Demo repo for "Agentic .NET in Action: Microsoft Agent Framework, Aspire and Foundry". The solution lives in `ArchitectureReviewer/` (see `ArchitectureReviewer/PLAN.md` for design and decisions).

## Stack

- .NET 10, Aspire, Microsoft Agent Framework (MAF), DevUI, Azure OpenAI / Microsoft Foundry.
- Solution file: `ArchitectureReviewer/ArchitectureReviewer.slnx`. Build with `dotnet build ArchitectureReviewer/ArchitectureReviewer.slnx`.
- Run via the AppHost: `dotnet run --project ArchitectureReviewer/ArchitectureReviewer.AppHost`.

## Projects

- `ArchitectureReviewer.ApiService`: hosts agents, workflows, DevUI and OpenAI Responses/Conversations endpoints. All agent wiring is in `Program.cs`.
- `ArchitectureReviewer.AppHost`: Aspire orchestration; forwards `AZURE_OPENAI_ENDPOINT` and `AZURE_OPENAI_DEPLOYMENT_NAME` to `apiservice` and adds the `/devui` link.
- `ArchitectureReviewer.ServiceDefaults`: shared OpenTelemetry, health checks, service discovery and HTTP resilience.
- `ArchitectureReviewer.Web` is out of scope; do not add it to the solution.

## Architecture

- Four specialist agents (`ArchitectureAgent`, `SecurityAgent`, `ReliabilityScalabilityAgent`, `CostOperationsAgent`) run concurrently in the `ReviewPanel` workflow (`AgentWorkflowBuilder.BuildConcurrent`).
- `ArchitectureReviewBoard` is a sequential workflow: `ReviewPanel` (as an agent) then `SynthesisAgent`.
- All agents share one `IChatClient` singleton. Register agents with `AddAIAgent` and workflows with `AddWorkflow(...).AddAsAIAgent()` so DevUI discovers them.
- Each specialist has a narrow scope and must not review other specialists' areas. Keep instructions short and single-purpose.
- `SynthesisAgent` output must include: Proposed Design, Rationale, Trade-offs, Risks, Mitigations, Open Questions, and how conflicting opinions were resolved.

## Conventions

- No hardcoded endpoints or secrets. `AZURE_OPENAI_ENDPOINT` is required; `AZURE_OPENAI_DEPLOYMENT_NAME` defaults to `gpt-5-mini`.
- Auth is `AzureCliCredential` for local development only (requires `az login`).
- Package versions are managed per project. MAF `Microsoft.Agents.AI.DevUI`, `Hosting` and `Hosting.OpenAI` are prereleases; keep them aligned with the `Microsoft.Agents.AI.OpenAI` 1.4.x line.
- Keep the HTTP resilience handler LLM-friendly (long attempt/total timeouts); the default 10 s timeout cuts off agent calls.
- Telemetry must include the `*Microsoft.Extensions.AI` and `*Microsoft.Extensions.Agents*` activity sources.
- DevUI is a development tool only; do not expose it in production configuration.
- Out of scope: RAG, tools, external integrations, custom UI, production hardening. Do not add them unless asked.

## Gotchas

- Extension methods like `AsAIAgent` on `AIProjectClient` live in the `Azure.AI.Projects` namespace; a missing `using` produces misleading overload errors.
- `OPENAI001` evaluation diagnostics are build errors by default; suppress with `<NoWarn>$(NoWarn);OPENAI001</NoWarn>` only where intentionally used.
- If composing a concurrent workflow as an agent inside `BuildSequential` fails on the installed packages, fall back to an explicit `WorkflowBuilder` graph (fan-out, fan-in aggregator executor, then `SynthesisAgent`).
