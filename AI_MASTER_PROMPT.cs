/*
You are a senior software architect and AI system designer.

Your task is to design a complete MCP (Model Context Protocol) for an AI coding agent
to build a WPF application that automates invoice processing from a government website.

========================
PROJECT DESCRIPTION
========================

This is an internal automation tool.

Main features:
1. Login to government invoice portal and reuse session
2. Navigate between tabs (sales invoices, purchase invoices)
3. Filter invoices by date
4. Click search and load results
5. Download XML invoice files
6. Extract ZIP files
7. Upload XML to another website
8. Download PDF invoices
9. Show logs and progress in UI

Tech stack:
- C# (.NET 6+)
- WPF (MVVM)
- Microsoft Playwright

========================
CRITICAL REQUIREMENT
========================

The automation MUST be STEP-BASED and CONFIG-DRIVEN.

DO NOT hardcode UI actions in code.

Instead:
- Define a JSON-based step system
- Each step represents a user action:
  click, fill, wait, download, loop

The system must:
- Allow easy modification when UI changes
- Support adding/removing steps without changing code
- Use variable placeholders like {{fromDate}}

========================
OUTPUT REQUIREMENTS
========================

Generate the following sections:

------------------------
1. TASK LIST
------------------------
- Break down the project into small tasks
- Each task must include:
  - goal
  - input/output
  - step-by-step instructions
- Tasks must be suitable for an AI coding agent

------------------------
2. SKILL LIST
------------------------
Define reusable skills such as:
- Playwright step execution
- WPF MVVM binding
- Logging system
- File processing (ZIP/XML)

Each skill must include:
- purpose
- rules
- examples

------------------------
3. WORKFLOW
------------------------
Define full execution flow:
- step-by-step actions
- validation after each step
- retry strategy
- error handling

------------------------
4. STEP ENGINE DESIGN
------------------------
Design a JSON-based automation system:

Include:
- Step schema (fields like Name, Action, Selector, Value, Retry, Timeout)
- Supported actions:
  click, fill, wait, download, loop
- Variable replacement system (e.g. {{fromDate}})
- Retry logic
- Logging per step

------------------------
5. SAMPLE JSON FLOW
------------------------
Generate a real example JSON flow for:
- Switching tab (sales invoice)
- Filling date filter
- Clicking search
- Waiting for table
- Looping rows
- Downloading XML

------------------------
6. ARCHITECTURE
------------------------
Define project structure:
- View (WPF)
- ViewModel
- Services (GovCrawler, StepExecutor, FileProcessor)
- Core (JobRunner)

Define responsibilities clearly.

========================
RULES
========================

- Be explicit and structured
- Avoid vague descriptions
- Use real UI actions like:
  "click button", "fill input", "wait for selector"
- Assume the agent has no prior knowledge
- Make everything modular and maintainable

========================
GOAL
========================

The output should allow an AI coding agent to:
- Generate code automatically
- Adapt to UI changes easily
- Maintain the system with minimal manual coding
*/