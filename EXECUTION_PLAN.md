# Execution Plan — Accountant Invoice Automation

**Source specification:** `MCP_DOCUMENT.md`  
**Purpose:** Ordered delivery with agent tooling, retries, and validation gates.

---

## 1. Execution phases (high level)

| Phase | Scope | Exit criterion |
|-------|--------|----------------|
| A | Repository structure, solution, packages, config skeleton | `dotnet build` green |
| B | Core domain + validation + variables + job orchestration | Unit tests green |
| C | Services: JSON load, Playwright execution, ZIP/XML | Build + targeted tests |
| D | WPF shell: MVVM, start/cancel, logs, flow path | App runs, manual smoke |
| E | Session storage, login flow hooks, packaging notes | Documented + optional UI command |

Phases **B–C** implement the step engine; **D** wires the operator UI; **E** completes spec tasks 1.5, 1.14.

---

## 2. Step-by-step tasks (from specification)

Each row is an executable work unit. **Depends on** lists prerequisite task IDs.

| ID | Spec ref | Task | Depends on | Validation |
|----|----------|------|------------|------------|
| T1 | 1.1 | Create solution, WPF App, Core, Services, Tests; nullable; `appsettings.json` | — | Build, App window |
| T2 | 1.2 | `AutomationFlow`, `AutomationStep`, retry/expect/loop models; `FlowValidator` | T1 | Deserialize sample JSON §5 |
| T3 | 1.3 | `FlowContext`, `JobParameters`, `IVariableResolver`, `VariableResolver` | T2 | Resolver unit tests |
| T4 | 1.12 | `JobRunner`, `JobResult`/`StepResult`, sequential steps, `expect`, cancellation | T2, T3 | Mock executor tests |
| T5 | 1.4 | `IAutomationPage` (Core), `PlaywrightAutomationPage`, `PlaywrightStepExecutor` | T1, T2 | Build (integration optional) |
| T6 | 1.4 | Loop: rows/count, child steps, `rowIndex`, `maxIterations` | T4, T5 | Flow with loop |
| T7 | 1.13 | `AppSettings` binding: flows path, login path, downloads, browser, automation defaults | T1 | Config loads |
| T8 | 1.10–1.11 | `MainViewModel`, `MainWindow`, DI, file log + UI log buffer | T4, T7 | Start/cancel, logs |
| T9 | 1.7, 1.9 | `FileProcessor`: ZIP safe extract, XML/PDF sniff | T1 | ZIP unit test |
| T10 | 1.8 | `upload`, `extractZip`, `press`, `selectOption` in executor | T5, T9 | Build |
| T11 | 1.5 | Storage state path in settings; load/save in browser factory; optional Test Login UI | T5, T7 | Document manual test |
| T12 | 1.14 | `README.md`: `playwright install`, publish notes | T1 | Doc present |

**Deferred to JSON-only (no extra code once T6 done):** Task 1.6 gov tabs/filters — operators edit `flows/*.json`.

---

## 3. Map each task to MCP / agent tools

In Cursor, “MCP tools” are the protocol surfaces available to the agent (filesystem, terminal, search, etc.). Use this mapping when automating or reviewing work.

| Task ID | Primary tools | How they are used |
|---------|---------------|-------------------|
| T1 | **run_terminal_cmd** (`dotnet new`, `dotnet add`, `dotnet build`) | Scaffold projects and verify compile |
| T1 | **Write** / **StrReplace** | Create `.csproj`, `App.xaml`, `Program`/`App.xaml.cs` |
| T2 | **Read** (`MCP_DOCUMENT.md` §4–5), **Write** | Model classes mirror schema |
| T3 | **Write**, **Grep** | Resolver + tests |
| T4 | **Write**, **SemanticSearch** (optional, repo-wide patterns) | `JobRunner` orchestration |
| T5 | **Write**, **run_terminal_cmd** | NuGet restore, Playwright package |
| T6–T10 | **Write**, **Read** | Cross-file consistency |
| T8 | **Write** | XAML + ViewModel |
| T11–T12 | **Write** | Settings + README |
| All | **ReadLints** | Fix IDE diagnostics on edited files |
| Verify | **run_terminal_cmd** (`dotnet test`, `dotnet build`) | Gates |

If using a **custom MCP server** (e.g. database or browser farm), map only **T5/T11** to that server; this repo’s default is local Playwright.

---

## 4. Retry strategies (prepared)

### 4.1 Step-level (JSON `retry`)

| Field | Behavior |
|-------|----------|
| `count` | Total attempts including the first try. Example: `count: 3` → up to 3 executions. |
| `backoffMs` | After failure on attempt *k*, delay `backoffMs[k-1]` ms before next attempt (if index exists); else repeat last entry or use global default delay. |

**Implementation rule:** If `backoffMs` length is less than `count - 1`, pad with last value or `1000` ms.

### 4.2 Global defaults (`appsettings.json`)

| Key | Purpose | Suggested default |
|-----|---------|-------------------|
| `Automation:DefaultRetries` | When step has no `retry` | `3` |
| `Automation:DefaultTimeoutMs` | Locator/operation timeout | `30000` |
| `Automation:RetryBackoffMs` | Global backoff array when step has no `backoffMs` | `[ 1000, 2000, 4000 ]` |

### 4.3 Non-retryable errors (configurable list)

| Pattern (substring match) | Action |
|---------------------------|--------|
| `invalid credentials` | Fail immediately; no retry |
| `401` / `403` (optional) | Fail job or step per product policy |

Stored as `Automation:NonRetryableSubstrings` array in settings.

### 4.4 `onError` on step

| Value | Behavior after final retry failure |
|-------|-----------------------------------|
| `fail` | Mark step failed; fail job (default) |
| `continue` | Log error; continue to next step |
| `abortJob` | Stop job immediately |

### 4.5 Loop child failure

| Policy | Behavior |
|--------|----------|
| Default | Abort loop and propagate failure (aligns with spec “abort loop default”) |
| `onError: continue` on child | Skip remaining children for that row? Spec: per-row **continue** — implement as continue to next row when child has `continue` |

**Implementation:** For loop children, honor each step’s `onError` the same as top-level.

### 4.6 Post-condition `expect` failure

Treat as step failure; apply the same retry policy as the main action for that step (per MCP §3.4).

### 4.7 Transient network / Playwright timeout

Retry with backoff; if all attempts exhausted → `onError` policy.

---

## 5. Execution log (maintenance)

| Date | Action | Outcome |
|------|--------|---------|
| 2026-03-22 | T1–T12 executed: solution, Core/Services/App, smoke flow, tests, README | `dotnet build` and `dotnet test` pass |

---

*This plan is executed by implementing tasks T1→T12 in order, adjusting only when a dependency forces a merge (e.g. T5 before T6 loop wiring).*
