# Model Context Protocol (MCP) Specification — Accountant Invoice Automation

**Project:** Internal WPF tool for government invoice portal automation  
**Stack:** C# (.NET 6+), WPF (MVVM), Microsoft Playwright  
**Core constraint:** Automation is **step-based** and **config-driven**. UI actions are **not** hardcoded in executable logic; they are expressed in JSON and executed by a generic engine.

This document is the system requirements and operational contract for an AI coding agent (and human maintainers) implementing and evolving the solution.

---

## 1. Tasks

Each task is sized for an AI coding agent to implement in one focused iteration. Every task lists **goal**, **inputs/outputs**, and **step-by-step instructions**.

---

### Task 1.1 — Solution and project skeleton

**Goal:** Create a maintainable .NET solution with WPF host, class libraries for core automation and services, and test project placeholders.

**Input:** This MCP document; target framework .NET 6 or later.

**Output:**
- Solution file (`.sln`) referencing:
  - `InvoiceAutomation.App` (WPF, MVVM)
  - `InvoiceAutomation.Core` (job orchestration, step models)
  - `InvoiceAutomation.Services` (Playwright, file I/O)
  - `InvoiceAutomation.Tests` (unit tests, optional integration harness)
- Nullable reference types enabled; consistent assembly naming.

**Step-by-step instructions:**
1. Create WPF project with `Application` startup, single main window shell.
2. Add class libraries; reference `Core` from `App` and `Services`; reference `Services` from `App` as needed.
3. Add NuGet: `Microsoft.Playwright`, `CommunityToolkit.Mvvm` (or chosen MVVM toolkit), `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging`, `Serilog` or built-in providers as specified by architecture.
4. Configure `appsettings.json` for paths, default flow file, log level, browser channel.
5. Verify build succeeds on clean machine after `dotnet restore` and Playwright browser install step documented in README (agent may add minimal README only if repo policy allows; otherwise document in this MCP’s workflow section).

**Validation:** `dotnet build` succeeds; App launches empty shell window.

---

### Task 1.2 — Domain models for steps and flows

**Goal:** Define immutable (or carefully mutable) C# types that deserialize from JSON and drive the step engine.

**Input:** Step Engine Design (Section 4) in this document.

**Output:**
- `AutomationFlow` (name, version, variables default map, ordered `Steps`).
- `AutomationStep` discriminated or property bag matching schema (Name, Action, Selector, Value, Retry, Timeout, Children for loops, etc.).
- `StepAction` enum or string constants: `click`, `fill`, `wait`, `download`, `loop` (and any extended actions agreed later).
- JSON serializer settings: case-insensitive property names, explicit null handling.

**Step-by-step instructions:**
1. Map every schema field from Section 4 to a property with XML doc comments referencing JSON name.
2. Add validation attributes or a dedicated validator that runs after deserialization (unknown actions fail fast).
3. Unit tests: deserialize sample JSON from Section 5 without error.

**Validation:** Tests pass; invalid JSON/action names produce clear exceptions.

---

### Task 1.3 — Variable replacement system

**Goal:** Implement `{{placeholder}}` resolution against a runtime context (user input, prior step outputs, environment).

**Input:** Flow JSON with placeholders; dictionary or strongly typed `FlowContext`.

**Output:**
- `IVariableResolver` / `VariableResolver` replacing `{{name}}` in string fields (Selector, Value, Url, Path, etc.) recursively where defined.
- Rules: missing variable → configurable behavior (fail or empty string per step option).
- Escape mechanism if literal `{{` needed (e.g. `{{\` or documented sentinel).

**Step-by-step instructions:**
1. Scan resolved strings with a regex for `{{...}}`.
2. Merge variable sources in order: flow defaults → job parameters → step outputs → environment overrides.
3. Log pre- and post-resolution for debug level only (avoid logging secrets).

**Validation:** Unit tests for nested keys, missing keys, multiple placeholders in one string.

---

### Task 1.4 — Playwright step executor service

**Goal:** Execute JSON-defined steps against a live browser using Playwright, without embedding portal-specific selectors in C#.

**Input:** Resolved `AutomationStep`, `IPage` or `IBrowserContext`, cancellation token.

**Output:** Step result (success/failure, optional artifacts path, timing, error message); events for UI logging.

**Step-by-step instructions:**
1. Implement `IStepExecutor` with method `ExecuteAsync(AutomationStep step, StepExecutionContext ctx)`.
2. For `click`: `page.Locator(selector).ClickAsync()` with timeout from step or default.
3. For `fill`: clear/fill pattern appropriate for control type; support `Value` after variable resolution.
4. For `wait`: wait for selector visible/hidden/network idle per step sub-type if schema provides `WaitUntil` or similar.
5. For `download`: trigger click that starts download; save to configured folder with naming pattern supporting variables.
6. For `loop`: iterate child steps over rows from table selector or numeric range per schema; expose row index to variables `{{rowIndex}}`, `{{cellValue:column}}` if specified.
7. Apply per-step `Retry` and `Timeout` from schema.

**Validation:** Integration test against local static HTML fixture or Playwright test page mimicking table/button/download.

---

### Task 1.5 — Session reuse and login flow

**Goal:** Log in to the government invoice portal once per job (or reuse storage state), then run downstream steps using the same context.

**Input:** Credentials from secure configuration or user prompt (never committed); login flow JSON or dedicated flow file.

**Output:** Authenticated `IBrowserContext` or storage state file path for reuse within the app session.

**Step-by-step instructions:**
1. Add optional `storageStatePath` in settings; if file exists, load context with saved state before login steps.
2. If not authenticated, run login sub-flow from JSON (navigate, fill user/password, click submit, wait dashboard).
3. On success, persist storage state to user app data folder (encrypted at rest if platform allows).
4. Expose “Test login” command in UI for operators.

**Validation:** Manual or recorded test: second launch skips login when state valid.

---

### Task 1.6 — Gov crawler orchestration (tabs, filters, search)

**Goal:** Drive navigation: sales vs purchase invoices, date filters, search, table ready — entirely via JSON flows.

**Input:** Parameters `fromDate`, `toDate`, `invoiceKind` (sales/purchase).

**Output:** Table visible; row count available to loop steps; logs for each step.

**Step-by-step instructions:**
1. Parameterize main flow JSON with `{{fromDate}}`, `{{toDate}}`, `{{tab}}` or separate flow files per tab.
2. Implement steps: switch tab (click tab label or href), fill date inputs, click search, wait for results table selector.
3. No portal-specific strings in C# beyond default config paths and flow file names.

**Validation:** Run against portal (or staging); confirm table selector matches after UI changes by editing JSON only.

---

### Task 1.7 — XML download and ZIP extraction

**Goal:** Download XML invoice files from portal rows, extract ZIP archives when present, normalize output file paths for upload.

**Input:** Download directory; loop step providing file names or links.

**Output:** Flat or structured folder of `.xml` files ready for upload step.

**Step-by-step instructions:**
1. Use `download` action in JSON to save with `{{invoiceId}}` or similar in filename.
2. Implement `FileProcessor` service: detect `.zip`, extract with `System.IO.Compression`, optional virus scan hook (no-op default).
3. Log each file path and size.

**Validation:** Unit tests with sample ZIP; verify duplicate name handling.

---

### Task 1.8 — Upload XML to secondary website

**Goal:** After extraction, upload XML to another site (endpoint and selectors in JSON or hybrid config).

**Input:** Resolved file paths from prior steps; target URL and form selectors in JSON flow.

**Output:** HTTP success confirmation or UI confirmation per site behavior.

**Step-by-step instructions:**
1. Either extend step engine with `upload` action or model upload as sequence of `fill` + `click` using file input `setInputFiles` in Playwright via a dedicated action if needed.
2. Keep selectors and URLs in JSON.
3. Retry on transient network errors per global retry policy.

**Validation:** Dry-run mode that logs would-be uploads without sending bytes (optional flag).

---

### Task 1.9 — PDF invoice download

**Goal:** Download PDF invoices from portal or secondary site using the same step engine.

**Input:** PDF link or row action from loop; download folder.

**Output:** PDF files on disk; log entries.

**Step-by-step instructions:**
1. Reuse `download` action; ensure content-type or extension validation in `FileProcessor`.
2. Name files with invoice id and date from variables.

**Validation:** File exists, non-zero size, header `%PDF`.

---

### Task 1.10 — WPF MVVM main UI (progress and logs)

**Goal:** Operator-facing UI showing job status, current step, log tail, cancel button, parameter inputs (dates, tab).

**Input:** `IJobRunner` events; `ObservableCollection` of log lines.

**Output:** Responsive window bound to ViewModel; no business logic in code-behind.

**Step-by-step instructions:**
1. Main View: date pickers, combo/radio for sales vs purchase, Start/Cancel, progress bar, log ListBox or DataGrid with auto-scroll.
2. ViewModel commands: `StartAutomationCommand`, `CancelCommand`, `OpenLogsFolderCommand`.
3. Subscribe runner events on UI thread via dispatcher.

**Validation:** Start/cancel updates UI; logs appear in order.

---

### Task 1.11 — Logging system

**Goal:** Structured logging to file and UI with correlation id per job run.

**Input:** Microsoft.Extensions.Logging abstractions.

**Output:** Rolling file logs; in-memory buffer for UI; log level per namespace.

**Step-by-step instructions:**
1. Register `ILogger<T>` in DI.
2. For each step: log Start/End with Name, Action, duration, success/failure.
3. Never log passwords or full storage state; mask tokens.

**Validation:** Log file created; UI shows last N lines.

---

### Task 1.12 — Job runner (Core)

**Goal:** Single entry point that loads JSON flow, merges parameters, runs steps sequentially (respecting loops), handles cancellation and global errors.

**Input:** Flow file path, `JobParameters`, services.

**Output:** `JobResult` with aggregate status and per-step results.

**Step-by-step instructions:**
1. Implement `JobRunner.RunAsync(...)` as described in Workflow (Section 3).
2. Inject `IStepExecutor`, `IVariableResolver`, `IFlowLoader`.
3. Support loading flow from embedded resource or file path in settings.

**Validation:** End-to-end test with mock executor verifying order and cancellation.

---

### Task 1.13 — Configuration and flow file management

**Goal:** Operators can point the app at different JSON flows without rebuild.

**Input:** `appsettings.json`, optional user overrides.

**Output:** File picker or fixed `Flows/` directory; validation on load.

**Step-by-step instructions:**
1. Settings: `Flows:DefaultPath`, `Flows:LoginPath`, `Browser:Headless`, `Downloads:RootPath`.
2. Validate JSON schema on load (optional JSON Schema file).

**Validation:** Invalid flow shows user-readable error in UI.

---

### Task 1.14 — Packaging and deployment

**Goal:** Publish self-contained or framework-dependent build for internal distribution.

**Input:** Target RID if self-contained.

**Output:** Published folder with Playwright install script or documented `pwsh` command.

**Step-by-step instructions:**
1. Document browser binaries acquisition (`playwright install`).
2. Version assembly with git hash or semver.

**Validation:** Clean VM smoke test.

---

## 2. Skills

Reusable **skills** are conventions and modules the agent must apply consistently. Each skill has **purpose**, **rules**, and **examples**.

---

### Skill 2.1 — Playwright step execution

**Purpose:** Execute browser interactions solely from resolved step definitions so portal UI changes require JSON edits, not recompilation.

**Rules:**
1. All selectors, URLs, visible text targets, and wait conditions live in JSON (or external selector map JSON referenced by step id), not as string literals in executor switch cases beyond action type dispatch.
2. Use Playwright auto-waiting; honor step `Timeout` in milliseconds; default global timeout from configuration.
3. Prefer resilient selectors: `data-testid` if available; otherwise stable role/name; CSS/XPath only when necessary and documented in JSON comments if parser allows (JSON5 optional) or parallel `description` field in step object.
4. One step = one logical user intention (e.g. “click search” not “click five things” unless modeled as child steps).
5. Downloads must use Playwright’s download API (`RunAndWaitForDownloadAsync` pattern) wrapped inside executor for `download` action.
6. Loops must not leak locators across iterations without re-querying; refresh locator from `page` each iteration.

**Examples:**
- **Click search:** Action `click`, Selector `#searchButton` or `role=button[name='Search']`.
- **Fill date:** Action `fill`, Selector `input[name='fromDate']`, Value `{{fromDate}}`.
- **Wait for table:** Action `wait`, Selector `table#results`, sub-property `state: visible`.
- **Download row XML:** Action `download`, Selector `tr:nth-child({{rowIndex}}) a.download-xml`, `saveAs`: `{{downloadsRoot}}\{{invoiceId}}.xml`.

---

### Skill 2.2 — WPF MVVM binding

**Purpose:** Keep UI testable and separated from automation and file I/O.

**Rules:**
1. No database or Playwright types in Views; Views only bind to ViewModels.
2. ViewModels depend on abstractions (`IJobRunner`, `ILogger`, `IDialogService` if used).
3. Long-running work uses `AsyncRelayCommand` or equivalent; disable Start while running; `CancellationTokenSource` owned by runner or VM coordinator.
4. All operator-visible strings use resources if localization is anticipated; minimum is centralized constants for labels.
5. Use `INotifyPropertyChanged` consistently; avoid `async void` except event handlers.

**Examples:**
- `public IAsyncRelayCommand StartCommand { get; }` → calls `_runner.RunAsync(parameters, ct)`.
- Log collection: `ObservableCollection<LogLineViewModel>` bound to ListBox; append on background with `Application.Current.Dispatcher.Invoke`.

---

### Skill 2.3 — Logging system

**Purpose:** Audit automation for compliance and debugging without exposing secrets.

**Rules:**
1. Log levels: `Trace` for selector resolution (non-prod), `Information` for step boundaries, `Warning` for retries, `Error` for failures with exception type, not full stack in UI (full stack in file).
2. Correlation: each job run has `JobId` (GUID) in every log scope.
3. PII: mask national IDs, tax numbers if they appear in URLs or filenames.
4. File logging: rolling daily or size-based; path under `%LocalAppData%\InvoiceAutomation\logs`.
5. UI buffer: ring buffer max 500 lines to prevent memory growth.

**Examples:**
- `logger.LogInformation("Step {StepName} ({Action}) started", step.Name, step.Action);`
- `logger.LogWarning(ex, "Step {StepName} failed attempt {Attempt} of {Max}", name, attempt, max);`

---

### Skill 2.4 — File processing (ZIP / XML)

**Purpose:** Normalize downloaded artifacts for upload and archival.

**Rules:**
1. Extract ZIPs to a subfolder named after archive stem; handle encoding and path traversal (reject `..` entries).
2. After extraction, delete or quarantine ZIP per configuration.
3. Validate XML with optional schema (XSD path in settings); invalid XML moves to `failed\` with reason logged.
4. Atomic writes: download to `.tmp` then rename.
5. Maximum file size limits from config to avoid disk exhaustion.

**Examples:**
- Extract: `ZipFile.ExtractToDirectory(archive, dest, overwrite: false)` with per-entry path check.
- Upload prep: enumerate `*.xml` in folder, order by name, attach each in loop step or batch API per JSON flow.

---

### Skill 2.5 — JSON flow authoring

**Purpose:** Allow non-developers or agents to change automation by editing data.

**Rules:**
1. Every flow file has `version` and `name` for traceability.
2. Document required variables at top of file in `variables` object with default empty strings or sample values for dev only.
3. Prefer small included flows: `login.json`, `sales-invoices.json`, `purchase-invoices.json` orchestrated by parent flow `import` if engine supports it; if not, duplicate minimal steps until import is implemented.
4. After portal deploy, update selectors in JSON and run dry-run with headful browser.

**Examples:**
- Variables block: `"variables": { "fromDate": "", "toDate": "", "tab": "sales" }`.
- Step naming: `"name": "Open sales tab"` for log clarity.

---

### Skill 2.6 — Security and secrets

**Purpose:** Internal tool still handles credentials and sensitive invoices.

**Rules:**
1. Credentials from Windows Credential Manager, user prompt, or environment — not source control.
2. Use HTTPS only; optional certificate pinning only if mandated (document exception).
3. Restrict downloaded files folder ACLs to current user.
4. Clear sensitive variables from memory after job when feasible (replace references, avoid logging).

---

## 3. Workflow

This section defines the **full execution flow** from application start to job completion, including **validation after each step**, **retry strategy**, and **error handling**.

---

### 3.1 Application bootstrap

1. **Start app:** Load configuration, configure DI, initialize logging, ensure directories exist (`Downloads`, `Logs`, `Extracted`, `Failed`).
2. **Playwright:** Create `Playwright` instance; launch browser per settings (Chromium default); create context with viewport and optional storage state.
3. **Validation:** Browser launches; context is non-null; log file writable.

---

### 3.2 Operator input phase

1. Operator sets **from date**, **to date**, **invoice type** (sales / purchase), optional **flow override path**.
2. Operator clicks **Start**.
3. **Validation:** Dates parse; `from <= to`; flow file exists; JSON parses to `AutomationFlow`.

If validation fails: show inline errors; do not start browser job.

---

### 3.3 Job initialization

1. Generate **JobId**; push “Job started” to UI log.
2. Build **FlowContext**: merge default variables from JSON with UI parameters (`fromDate`, `toDate`, `tab`, `downloadsRoot`, etc.).
3. Load ordered step list; flatten or expand templates if engine supports includes.
4. **Validation:** All referenced variables in steps exist in context or have defaults; unknown actions rejected.

---

### 3.4 Step execution loop (sequential)

For each step in order (unless inside a **loop**, which is a mini-workflow):

1. **Pre-step**
   - Resolve variables in all string fields of the step.
   - Log: step name, action, resolved selector (mask if contains token).
   - If step is disabled (`enabled: false`), skip with log.

2. **Execute with retry**
   - Let `attempt = 1`, `max = step.Retry?.Count ?? globalDefault` (e.g. 3).
   - Let `delay = step.Retry?.BackoffMs ?? [1000, 2000, 4000]` exponential or fixed per config.
   - **Attempt execution** via `IStepExecutor`:
     - On success: record `StepResult` (duration, optional output variables); goto **Post-step validation**.
     - On failure: if `attempt < max`, log warning, wait `delay[attempt-1]`, increment `attempt`, retry.
     - If `attempt == max`: mark step failed; goto **Failure policy**.

3. **Post-step validation**
   - If step defines `expect` (e.g. selector visible, URL contains fragment): run assertion.
   - On assertion failure: treat as step failure; apply same retry policy if configured for assertions.

4. **Cancellation**
   - If `CancellationToken` triggered: abort after current atomic Playwright operation; log “Cancelled at step X”; set job status **Cancelled**.

---

### 3.5 Loop workflow

1. Evaluate **loop** action: determine iterations (e.g. row count from `table tbody tr`, or `count` property).
2. For `i = 1..N`:
   - Push scope variables: `rowIndex`, optional `rowLocator` token.
   - Execute child steps in order using same retry and validation rules.
   - If child step fails: apply `onRowError` policy — **abort loop** (default), **continue**, or **retry row** (max per row).
3. Pop scope; continue parent flow.

---

### 3.6 Download and file workflow

1. **Download action:** Complete Playwright download; save with resolved path; verify file exists and size > 0.
2. **Post-download:** If extension `.zip`, queue for extraction (synchronous in same job or next steps via `extract` action if implemented).
3. **Validation:** File magic bytes (ZIP PK, XML `<?xml`); on failure log and move to `Failed`.

---

### 3.7 Upload phase (secondary site)

1. Run flow segment or steps dedicated to upload (file input + submit).
2. **Validation:** HTTP status or success selector wait per JSON.
3. On failure: retry with backoff; classify permanent vs transient errors (4xx vs 5xx/network).

---

### 3.8 Job completion

1. Persist storage state if configured.
2. Emit summary: total steps, failures, files downloaded/uploaded counts.
3. Set UI status **Completed** or **Failed**; re-enable **Start**.

---

### 3.9 Global error handling

| Error class | Handling |
|-------------|----------|
| JSON / config | Block start; message box + log |
| Playwright timeout | Step retry then fail job or continue per step `onError: continue` |
| Network / DNS | Retry with backoff; fail if unrecoverable |
| Disk full | Fail immediately; log critical |
| Unauthorized portal | Fail login flow; prompt re-auth |
| Cancelled by user | Cooperative cancel; safe browser close |

Unhandled exceptions: log full stack to file; show sanitized message in UI.

---

### 3.10 Retry strategy summary

- **Step-level:** `Retry.Count`, `Retry.BackoffMs` array or `Retry.ExponentialBaseMs`.
- **Global default:** From `appsettings.json` under `Automation:DefaultRetries`.
- **Non-retryable:** Configurable list of error substrings (e.g. “invalid credentials”).
- **Idempotency:** Steps should be safe to repeat where possible (e.g. fill before click search, not double-submit payment unless allowed).

---

## 4. Step Engine Design

The **step engine** is the heart of the system: JSON describes *what* to do; C# describes *how* actions are mapped to Playwright and infrastructure.

---

### 4.1 Design principles

1. **Declarative:** Steps are data. Adding a button click is adding a JSON object, not a code change.
2. **Deterministic resolution:** Variables resolve in a documented order; same inputs yield same resolved steps.
3. **Observable:** Every step emits structured log events and optional telemetry.
4. **Extensible:** New actions register one handler without changing unrelated code.

---

### 4.2 Top-level flow object

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | yes | Human-readable flow name for logs and UI |
| `version` | string | yes | Semantic version of the flow schema or content |
| `description` | string | no | Operator-facing notes |
| `variables` | object | no | Default string values for placeholders |
| `steps` | array | yes | Ordered list of step objects |

---

### 4.3 Step object schema (core fields)

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `name` | string | yes | Stable identifier for logs and jump labels |
| `action` | string | yes | One of: `click`, `fill`, `wait`, `download`, `loop`, `navigate`, `press`, `selectOption`, `extractZip`, `upload`, `script` (optional extensions) |
| `enabled` | boolean | no | Default `true`; if `false`, skip step |
| `selector` | string | conditional | Playwright selector; required for most DOM actions |
| `value` | string | conditional | Text to type, URL to navigate, key to press |
| `timeoutMs` | integer | no | Override default timeout for this step |
| `waitUntil` | string | no | For `navigate`: `load`, `domcontentloaded`, `networkidle` |
| `expect` | object | no | Post-condition assertion (see 4.6) |
| `retry` | object | no | Per-step retry overrides |
| `onError` | string | no | `fail` (default), `continue`, `abortJob` |
| `output` | object | no | Map extracted values to variable names (future) |
| `children` | array | no | Nested steps for `loop` only |
| `comment` | string | no | Ignored by engine; for authors |

**Retry object:**

| Field | Type | Description |
|-------|------|-------------|
| `count` | integer | Max attempts including first try |
| `backoffMs` | array of int | Delay before each retry after first failure |

---

### 4.4 Supported actions (minimum set)

#### `navigate`

- **Purpose:** Go to URL.
- **Fields:** `value` (URL with optional `{{vars}}`), `waitUntil`, `timeoutMs`.
- **Implementation:** `page.GotoAsync(resolvedUrl, options)`.

#### `click`

- **Purpose:** Click element (button, tab, link).
- **Fields:** `selector`, `timeoutMs`, optional `button` (`left`, `right`), `modifiers`.
- **Implementation:** `Locator.ClickAsync()`.

#### `fill`

- **Purpose:** Fill input or textarea.
- **Fields:** `selector`, `value` (supports `{{fromDate}}`), optional `clearFirst` (default true).
- **Implementation:** `Locator.FillAsync()` after optional clear.

#### `wait`

- **Purpose:** Wait until condition holds.
- **Variants (via `waitKind` or inferred):**
  - `selectorVisible` — default: selector appears visible.
  - `selectorHidden`
  - `timeout` — fixed delay: `value` = milliseconds as string.
  - `networkIdle` — optional if supported.
- **Fields:** `selector`, `value` (for delay ms), `timeoutMs` as max wait.

#### `download`

- **Purpose:** Trigger download and save file.
- **Fields:** `selector` (element that initiates download), `savePath` (resolved path, use `{{var}}`), optional `timeoutMs` for download event.
- **Implementation:** Wrap click in `page.RunAndWaitForDownloadAsync`.

#### `loop`

- **Purpose:** Repeat child steps for each table row or fixed count.
- **Fields:**
  - `loopKind`: `rows` | `count`
  - `rowSelector`: e.g. `table#results tbody tr` (for `rows`)
  - `count`: integer or `{{var}}` (for `count`)
  - `children`: array of steps
  - `rowVariable`: name for 1-based index (default `rowIndex`)
  - `maxIterations`: safety cap (default 1000)
- **Implementation:** Count matching rows; for each index, set context variable and run `children`.

#### Optional extended actions

- **`press`:** `value` = key name (`Enter`, `Tab`).
- **`selectOption`:** dropdown by `selector` and `value` as option label or value.
- **`extractZip`:** `value` = path to zip; uses `FileProcessor`.
- **`upload`:** `selector` file input, `value` = file path pattern or single path.

---

### 4.5 Variable replacement system

**Syntax:** `{{variableName}}` in any string field of a step (including nested objects if engine walks tree).

**Sources (merge order, later overrides earlier):**

1. Flow file `variables` defaults  
2. Job parameters from UI / API  
3. Built-ins: `downloadsRoot`, `jobId`, `now:yyyyMMdd` (if supported)  
4. Loop scope: `rowIndex`, custom extracted vars  

**Rules:**
- Unknown placeholder: if `strictVariables: true` in flow, throw; else empty string and warning log.
- Escaping: document `{{{{` → literal `{{` if implemented; otherwise forbid `{{` in literals.

**Post-resolution:** Trim strings; normalize path separators on Windows for file paths.

---

### 4.6 Expect (post-condition) object

| Field | Type | Description |
|-------|------|-------------|
| `selector` | string | Element that must exist |
| `state` | string | `visible`, `hidden`, `attached` |
| `urlContains` | string | Current URL must contain substring |
| `timeoutMs` | integer | Max time to wait for expectation |

---

### 4.7 Logging per step

For each step execution the engine emits:

1. **StepStart:** jobId, stepName, action, attempt number  
2. **StepResolved:** debug-only dump of resolved fields (exclude secrets)  
3. **StepEnd:** success, durationMs, output paths  
4. **StepFailed:** exception type, message, selector, attempt  

Correlation: all entries share `jobId` and sequential `stepOrdinal`.

---

### 4.8 Concurrency and threading

- Single browser **page** per job unless flow defines new page (extension).
- UI thread never blocked: all Playwright work on background task with `ConfigureAwait(false)` in library code; marshal to UI only for log append.

---

### 4.9 Versioning and migration

- `flow.version` compared to engine `SupportedFlowVersion`; mismatch → warning or hard fail per policy.
- Deprecate fields with backward-compatible readers for two minor versions.

---

## 5. Sample JSON Flow

Below is a **concrete example** flow file (e.g. `flows/sales-invoices-download.json`) illustrating: **switch tab to sales**, **fill date filter**, **click search**, **wait for table**, **loop rows**, **download XML**. Selectors are **illustrative**; replace with real portal selectors when implementing.

```json
{
  "name": "Sales invoices — filter, search, download XML",
  "version": "1.0.0",
  "description": "Opens sales tab, applies date range, searches, downloads XML per row.",
  "variables": {
    "fromDate": "",
    "toDate": "",
    "downloadsRoot": "C:\\\\InvoiceAutomation\\\\Downloads",
    "resultsTable": "#invoice-results",
    "tabSales": "a[href*='sales']",
    "fromInput": "input#date-from",
    "toInput": "input#date-to",
    "searchButton": "button:has-text('Search')"
  },
  "steps": [
    {
      "name": "Ensure base URL",
      "action": "navigate",
      "value": "https://portal.example.gov/invoices",
      "waitUntil": "domcontentloaded",
      "timeoutMs": 60000,
      "retry": { "count": 3, "backoffMs": [ 2000, 5000, 10000 ] }
    },
    {
      "name": "Open sales invoice tab",
      "action": "click",
      "selector": "{{tabSales}}",
      "timeoutMs": 15000,
      "expect": {
        "selector": "{{resultsTable}}",
        "state": "visible",
        "timeoutMs": 10000
      }
    },
    {
      "name": "Fill from date",
      "action": "fill",
      "selector": "{{fromInput}}",
      "value": "{{fromDate}}",
      "timeoutMs": 10000
    },
    {
      "name": "Fill to date",
      "action": "fill",
      "selector": "{{toInput}}",
      "value": "{{toDate}}",
      "timeoutMs": 10000
    },
    {
      "name": "Click search",
      "action": "click",
      "selector": "{{searchButton}}",
      "timeoutMs": 15000
    },
    {
      "name": "Wait for results table body",
      "action": "wait",
      "selector": "{{resultsTable}} tbody tr",
      "timeoutMs": 120000,
      "comment": "Waits until at least one row exists or timeout if zero results"
    },
    {
      "name": "Loop result rows and download XML",
      "action": "loop",
      "loopKind": "rows",
      "rowSelector": "{{resultsTable}} tbody tr",
      "rowVariable": "rowIndex",
      "maxIterations": 500,
      "retry": { "count": 2, "backoffMs": [ 1000, 2000 ] },
      "children": [
        {
          "name": "Click XML download in row",
          "action": "download",
          "selector": "{{resultsTable}} tbody tr:nth-child({{rowIndex}}) a.download-xml",
          "savePath": "{{downloadsRoot}}\\\\{{fromDate}}_{{toDate}}\\\\row_{{rowIndex}}.xml",
          "timeoutMs": 60000,
          "onError": "continue",
          "comment": "Continue other rows if one download fails"
        },
        {
          "name": "Brief pause between rows",
          "action": "wait",
          "value": "300",
          "comment": "value interpreted as fixed delay milliseconds for wait-kind delay",
          "timeoutMs": 5000
        }
      ]
    },
    {
      "name": "Done banner wait",
      "action": "wait",
      "selector": ".job-complete-toast",
      "timeoutMs": 5000,
      "enabled": false,
      "comment": "Enable if portal shows a completion toast"
    }
  ]
}
```

**Notes for implementers:**

1. **`nth-child({{rowIndex}})`** — Engine must resolve `rowIndex` before building the selector; confirm Playwright accepts the resolved selector (1-based vs 0-based: align `rowIndex` convention in loop implementation with CSS `nth-child`).
2. **Zero rows:** `wait` on `tbody tr` may timeout; consider optional `onError: continue` on that wait or a separate `expect` with shorter timeout and branch (future `if` step) — until then, document that empty results show as step failure.
3. **ZIP:** Add follow-on flow or steps with `extractZip` after download if files are zipped; not shown in minimal sample.
4. **Purchase tab:** Duplicate flow with `tabSales` variable pointing to purchase tab selector or separate `variables.tab` driven from UI.

---

## 6. Architecture

This section defines **project structure**, **layers**, and **clear responsibilities** for View, ViewModel, Services, and Core.

---

### 6.1 Layered overview

```
┌─────────────────────────────────────────────────────────────┐
│                    InvoiceAutomation.App                     │
│  Views (XAML)  │  ViewModels  │  DI bootstrap  │  Resources │
└───────────────────────────┬─────────────────────────────────┘
                            │ references
┌───────────────────────────▼─────────────────────────────────┐
│                 InvoiceAutomation.Core                       │
│  JobRunner  │  Models (Flow, Step)  │  IVariableResolver     │
│  Interfaces │  JobResult / StepResult                        │
└───────────────────────────┬─────────────────────────────────┘
                            │ implemented by
┌───────────────────────────▼─────────────────────────────────┐
│              InvoiceAutomation.Services                      │
│  PlaywrightStepExecutor  │  FlowLoader  │  GovCrawlerService │
│  FileProcessor (ZIP/XML) │  Logging adapters                │
└─────────────────────────────────────────────────────────────┘
```

**Dependency rule:** `App` → `Core` + `Services`; `Services` → `Core`; `Core` has **no** dependency on Playwright or WPF.

---

### 6.2 InvoiceAutomation.App (View — WPF)

**Responsibilities:**
- XAML layouts: main window, optional settings dialog, log viewer styling.
- Resource dictionaries: colors, fonts, spacing consistent with internal tooling.
- `App.xaml.cs`: host builder, DI registration, global exception UI hook.

**Does not:**
- Parse JSON flows, call Playwright, or contain selector strings for the portal.

---

### 6.3 ViewModels (in App or separate `InvoiceAutomation.App.ViewModels` folder)

**Responsibilities:**
- `MainViewModel`: bindable properties (`FromDate`, `ToDate`, `InvoiceKind`, `IsRunning`, `StatusMessage`, `Logs`, `Progress`).
- Commands: start, cancel, open folders, browse flow file.
- Subscribe to `IJobRunner` events and update collections on UI thread.

**Does not:**
- Reference `IBrowser` directly; only `IJobRunner` and configuration abstractions.

---

### 6.4 InvoiceAutomation.Core

**Components:**

| Component | Responsibility |
|-----------|----------------|
| `JobRunner` | Orchestrates flow load, variable merge, step iteration, cancellation, aggregate `JobResult`. |
| `AutomationFlow` / `AutomationStep` | JSON DTOs and validation. |
| `IFlowLoader` | Load flow from path or stream. |
| `IStepExecutor` | Contract: execute one resolved step in browser context. |
| `IVariableResolver` | Replace `{{placeholders}}` from `FlowContext`. |
| `FlowContext` | Dictionary-like scoped store for variables and loop counters. |
| `JobParameters` | Strongly typed operator inputs passed into resolver. |

**JobRunner** is the **single orchestration brain**; it does not know Playwright APIs.

---

### 6.5 InvoiceAutomation.Services

#### GovCrawlerService (optional façade)

**Responsibility:** High-level operations if needed beyond raw steps (e.g. precompute session health). Prefer keeping behavior in JSON; use this only for non-UI integration (API calls) if added later.

**Default:** Thin wrapper or empty if all crawling is step-driven.

#### PlaywrightStepExecutor (implements `IStepExecutor`)

**Responsibility:**
- Map `action` string to Playwright operations.
- Manage downloads directory creation, file naming.
- Apply timeouts and retries delegated from `JobRunner` or self-contained per design (document single owner — recommend `JobRunner` owns retry loop, executor throws on failure).

#### FileProcessor

**Responsibility:**
- ZIP extract, XML validation, move to quarantine, path normalization.
- Used by steps `extractZip` or by post-processing hook after job.

#### FlowLoader

**Responsibility:**
- Read UTF-8 JSON, deserialize to `AutomationFlow`, run structural validation.

#### Logging

**Responsibility:**
- Register Serilog or file providers; optional `IUiLogSink` that pushes to `MainViewModel` via event aggregator or callback interface defined in Core.

---

### 6.6 External configuration

| Key | Purpose |
|-----|---------|
| `Flows:Default` | Path to default main flow JSON |
| `Flows:Login` | Optional login flow path |
| `Downloads:Root` | Base download directory |
| `Browser:Headless` | bool |
| `Browser:Channel` | chromium, msedge, etc. |
| `Automation:DefaultTimeoutMs` | Global step timeout |
| `Automation:DefaultRetries` | Global retry count |

---

### 6.7 Typical runtime sequence

1. User clicks Start → `MainViewModel` calls `IJobRunner.RunAsync(flowPath, parameters, ct)`.
2. `JobRunner` loads flow via `IFlowLoader`, builds `FlowContext`, iterates steps.
3. For each step, `JobRunner` resolves variables → `IStepExecutor.ExecuteAsync` with `IPage`.
4. Executor performs Playwright action; returns or throws.
5. `JobRunner` handles retry, `onError`, loop children, logs via `ILogger`.
6. On completion, ViewModel resets `IsRunning` and shows summary.

---

### 6.8 Testing strategy

- **Core:** unit tests for resolver, validation, job loop with fake executor.
- **Services:** integration tests with Playwright against local HTML fixtures.
- **App:** UI tests optional; manual checklist for releases.

---

### 6.9 Extension points

- New `action` types: register in executor dictionary.
- New variable sources: implement `IVariableSource` and compose into resolver.
- Additional flows: new JSON files under `flows/` with no code change.

---

## Document control

| Item | Value |
|------|--------|
| Derived from | `AI_MASTER_PROMPT.cs` project requirements |
| Intended use | AI agent code generation, human architecture reference |
| Maintenance | Update JSON schema and tasks when portal or policies change |

---

*End of MCP document.*
