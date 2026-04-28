# Accountant Invoice (WPF automation)

Internal tool skeleton: **config-driven** JSON steps executed with **Playwright**, orchestrated by **`JobRunner`** (see `MCP_DOCUMENT.md`).

## Prerequisites

- .NET 8 SDK
- Windows (WPF)

## Build

```powershell
dotnet build
```

## Playwright browsers

After the first successful build of the app, install browser binaries (paths vary by configuration):

```powershell
Set-Location "src\InvoiceAutomation.App\bin\Debug\net8.0-windows"
.\playwright.ps1 install
```

Or from the repo root after build:

```powershell
pwsh "src\InvoiceAutomation.App\bin\Debug\net8.0-windows\playwright.ps1" install
```

## Run

```powershell
dotnet run --project src\InvoiceAutomation.App
```

Default flow: `flows/smoke.json` (opens `https://example.com` for engine smoke tests).

**Production portals you are using:**

| Role | URL |
|------|-----|
| GDT e-invoice lookup | [https://hoadondientu.gdt.gov.vn/tra-cuu/tra-cuu-hoa-don](https://hoadondientu.gdt.gov.vn/tra-cuu/tra-cuu-hoa-don) |
| Tra cứu / NĐ123 site | [https://tracuuhoadon.vn/](https://tracuuhoadon.vn/) |

Template flows (edit selectors in JSON only, not in C#):

- `flows/gdt-tra-cuu.json` — opens the GDT tra cứu page.
- `flows/gdt-mua-vao-tra-cuu.json` — full template: login → tab **mua vào** → **Kết quả kiểm tra** → dates (`{{fromDateVi}}` / `{{toDateVi}}`, `dd/MM/yyyy`) → **Tìm kiếm** → table wait → optional bulk export / per-row loop. Edit **variables** selectors after inspecting the live page.
- `flows/tracuuhoadon.json` — opens tracuuhoadon.vn.

Set `Flows:DefaultPath` in `appsettings.json` to e.g. `flows/gdt-tra-cuu.json` when you are working on the tax portal.

## Configuration

`src/InvoiceAutomation.App/appsettings.json`:

- **Automation** — default timeouts, retries, backoff, non-retryable error substrings.
- **Browser** — `Headless`, optional `Channel`, `StorageStatePath` for session reuse.
- **Flows** — `DefaultPath`, optional `LoginPath` (used by **Test login flow** in the UI).
- **Downloads** — `RootPath` (empty = `%LocalAppData%\InvoiceAutomation\Downloads`).

## Solution layout

- `InvoiceAutomation.App` — WPF UI, DI, logging.
- `InvoiceAutomation.Core` — models, `JobRunner`, `VariableResolver`, options.
- `InvoiceAutomation.Services` — Playwright executor, `FlowLoader`, `FileProcessor`.
- `InvoiceAutomation.Tests` — unit tests for resolver, validation, job loop.

## Publish (internal)

```powershell
dotnet publish src\InvoiceAutomation.App -c Release -r win-x64 --self-contained false
```

Distribute the publish output; recipients still run `playwright.ps1 install` once on each machine.
