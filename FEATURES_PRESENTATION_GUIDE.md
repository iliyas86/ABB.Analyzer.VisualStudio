# ABB Analyze Feature Presentation Guide

## At a Glance

ABB Analyze is a Visual Studio extension for reviewing repository changes without leaving the IDE. It combines local quality checks, an optional Copilot-backed review, test guidance and execution, mutation testing, pull-request summary generation, and an Azure Boards checkpoint workflow.

The extension presents results in a dockable tool window. It uses the repository open in Visual Studio and displays the active repository and branch in its header.

## Suggested Opening

> ABB Analyze brings code review, test guidance, and change verification into Visual Studio. A developer can run a fast local check, ask a selected Copilot model to review changes, inspect findings and test recommendations, and use Azure Boards acceptance criteria as a checklist against the current Git diff.

## Main Review Workflow

### Local Check

**What it does:** Runs QUACK's local JSON check against the current repository and displays the returned quality findings and test guidance.

**What appears in the tool window:**

- Repository, branch, and review status
- Summary and risk information when provided by the analysis result
- Reasons and affected files
- Structured findings with severity, source, rule, location, and message
- Suggested tests and supported commands
- Raw JSON returned by QUACK

The local check and Copilot analysis are separate actions. The local check does not request the optional Copilot review.

### Copilot Analysis

**What it does:** Runs QUACK's review flow and then loads its structured check result. The selected model is passed to QUACK; if no specific model is selected, QUACK's configured default is used.

**What it provides:** An AI-generated risk, summary, reasons, affected files, and test recommendations when the provider returns an available review. Findings can come from either local rules or the Copilot review and are labelled by source.

AI review availability depends on the Copilot/QUACK configuration and authentication on the user's machine. The UI can fall back to local findings if AI review is unavailable.

### Model Selection

The model selector is populated from QUACK model discovery. The user can choose an available model for Copilot-backed analysis and Azure Boards checkpoint actions. If discovery is unavailable, the extension selects the QUACK default. Refresh Models retries discovery and loads the available model list.

## Review Views and Actions

### Overview

Shows the review summary, reasons, affected files, repository and branch context, and overall risk indicator. Double-click an affected file to open it in Visual Studio.

### Findings

Shows findings in a table with severity, source, rule, location, and message. Double-click a finding with a file location to navigate to the source location.

### Suggested Tests

Shows source files, recommended tests or commands, status, recommendation, and row actions to open a source/test file, copy its command/path, or run an approved command. The command field is editable so a developer can adapt a recommendation.

For security, execution is limited to supported `dotnet test` and `pytest` command forms; shell chaining and redirection characters are rejected. Test execution has a ten-minute timeout. Results and error output are shown in Test Results.

The extension also checks the repository for an existing conventionally named test file so an unchanged test file is not automatically mistaken for a coverage gap just because QUACK did not see it in the staged diff.

### Test Results

Displays the command, exit code, success/failure or no-matching-tests message, standard output, and error output from the most recent test run.

### Raw JSON

Displays the raw QUACK response for troubleshooting or integration inspection. The toolbar copy action copies the JSON to the clipboard.

### Copy Summary

Copies a Markdown review summary containing risk, repository, summary, reasons, and test guidance. It can be pasted into a pull request or another review document.

### Generate PR Description

Generates and copies a Markdown pull-request description from the current analysis model. When mutation results exist, mutation metrics are included as well.

### Navigation

Double-clicking a finding or affected file opens that location in Visual Studio. Suggested-test rows provide actions to open a relevant file or copy a command/path.

### Theme and Operation Feedback

The Azure Boards controls use Visual Studio theme resources so their surfaces and text follow the active IDE theme. Successful Azure Boards actions show a small green check on their button. Retrying an action or changing inputs clears checks that no longer represent the current state.

## Mutation Testing

The extension can run Stryker.NET mutation testing for the selected solution/project and display a mutation summary with killed, surviving, timed-out, no-coverage, ignored, and compile-error counts. Surviving mutants can be inspected by file, line, mutator, and description; selecting a survivor navigates to its source.

Two run modes are available:

- **Entire Solution:** runs Stryker without a changed-file scope.
- **Modified Files Only:** asks Stryker to scope analysis from `HEAD` using its Git integration.

The extension reads Stryker's JSON report and enriches surviving mutants with the source line when available. Mutation testing requires Stryker.NET to be available to `dotnet` or installed through the repository's local tool manifest. Mutation score is a test-strength signal; it is not proof that a feature meets its acceptance criteria.

## Azure Boards Checkpoint Workflow

The Azure Boards view connects to Azure DevOps Services, retrieves a work item, creates or drafts checkpoints, loads the current Git diff, and evaluates the checkpoints with Copilot.

### Connect with PAT

The user enters a masked Azure DevOps Personal Access Token. The recommended configuration is an organization-scoped PAT with only **Work Items (Read)** access and the shortest practical expiration. The extension clears the input after connecting and keeps the token in memory for the current Visual Studio session; it does not write the PAT to settings, source files, or logs.

The default sample fields point to organization `ABB-BCI-PCP`, project `PCP`, and work item `1443229`. They can be changed to another Azure DevOps Services work item.

### Fetch Work Item

Fetches the work item title, type, description, acceptance criteria, and bug repro steps where those fields are present. The content is displayed for review before checkpoints are made.

### Local Checkpoint

Builds editable checkpoint rows from the work item's acceptance criteria; if those are absent, it uses bug repro steps, then description text as a fallback. This is a local line-based extraction, not AI interpretation. Review and edit the rows because prose in a description may include goals or background rather than testable requirements.

### Copilot Checkpoint

Asks the selected Copilot model to draft concise candidate gates from the work item. The generated rows are editable and start as **Not evaluated**. Treat these as suggestions: validate that each gate is supported by the actual story before evaluating it.

### Refresh Diff

Loads the current saved Git working-tree diff, including staged changes, unstaged tracked changes, and untracked text files. Binary untracked files are omitted. The diff preview is capped for display, and Copilot evaluation also bounds the prompt size; omitted changes must not be treated as evidence.

### Evaluate

After the user confirms, the extension sends the work-item text, checkpoints, and current Git diff to the selected Copilot model. The GitHub Copilot CLI must be installed and signed in for the current Windows user. For this task, the extension disables Copilot's tools and built-in MCP servers; the model is asked to judge only the supplied context.

Each checkpoint receives one of these statuses:

- **Met:** Copilot judges the supplied changed code to support the checkpoint.
- **Not met:** Copilot judges the supplied diff to directly contradict the checkpoint or show an attempted implementation that fails it.
- **Inconclusive:** The supplied evidence is insufficient, incomplete, or not verifiable.

For a decisive result, Copilot must cite a file and an exact quote from an added diff line. The extension verifies that the file and quote occur in the submitted diff. Missing, malformed, or unverifiable evidence is downgraded to **Inconclusive**. The Evidence column wraps text and offers the full text on hover.

### Overall Gate

- **GATE PASSED:** every checkpoint is Met.
- **GATE FAILED:** at least one checkpoint is Not met.
- **REVIEW REQUIRED:** at least one checkpoint is Inconclusive or has not been evaluated.

Editing a checkpoint, fetching another work item, or refreshing the diff clears affected prior evaluations so a stale pass cannot be mistaken for a result on new inputs. Green/red decisions are AI-assisted review signals, not formal proof or a replacement for tests and engineering review.

### Data and Consent

The extension asks for confirmation before sending work-item text and changed code to GitHub Copilot for gate drafting/evaluation. The Azure DevOps PAT is used only for read-only work-item retrieval; it is not sent to Copilot as part of the prompt.

## Cancellation and Responsiveness

Local analysis, Copilot analysis, Copilot checkpoint drafting/evaluation, and mutation testing support cancellation. Test commands are bounded by a ten-minute timeout but are not connected to the Cancel button. The UI disables relevant actions while supported operations run and reports completion, cancellation, or failure in the status area. A cancelled or failed checkpoint evaluation does not retain a previous pass as though it applied to the attempted run.

## Presentation Demo: Work Item 1443229

1. Open the ABB Analyze tool window from the Visual Studio **View** selector.
2. Select **Azure Boards**.
3. Connect using an organization-scoped, read-only PAT, then fetch work item 1443229.
4. Show the story fields and explain that the local builder extracts existing criteria/repro text, while Copilot Checkpoint drafts candidate gates when the story needs interpretation.
5. Review or edit the gates, then select **Refresh diff** to show the current saved changes.
6. Select **Evaluate**, confirm the external AI data transfer, and inspect statuses and cited evidence.
7. Explain the overall gate: only all-Met passes; any Not met fails; incomplete evidence requires review.
8. Return to Overview/Findings or Suggested Tests to demonstrate how the Azure Boards view complements, rather than replaces, the normal code review and test workflows.

## Prerequisites and Boundaries

- Windows and a compatible Visual Studio installation with the ABB Analyze VSIX installed.
- A Git repository opened as the active Visual Studio solution for repository analysis and diff loading.
- A valid Azure DevOps Services PAT with Work Items (Read) scope to fetch boards work items.
- The GitHub Copilot CLI installed and signed in, plus an available selected model, for Copilot analysis and checkpoint actions.
- .NET test tooling for `dotnet test`, Python/pytest for `pytest`, and Stryker.NET for mutation testing, when those workflows are used.

Azure Boards integration currently targets Azure DevOps Services; Azure DevOps Server/on-premises authentication is not covered by this workflow. Checkpoint evaluation is based on supplied work-item text and a bounded current diff. It does not inspect the whole repository, run tests automatically as part of the gate, or prove that runtime behavior satisfies a requirement. Use test results, relevant existing code, and human review to close gaps that the diff cannot establish.

## Presenter Closing

> ABB Analyze gives developers a single Visual Studio workspace for fast local checks, Copilot-assisted review, test suggestions and execution, mutation testing, and work-item traceability. Its Azure Boards gate makes the relationship between story checkpoints and changed code visible, while preserving an inconclusive state when the evidence is not strong enough to claim success or failure.