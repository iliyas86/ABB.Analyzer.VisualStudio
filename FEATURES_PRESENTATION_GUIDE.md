# ABB Analyze: Simple Feature Guide

## 1. What Is ABB Analyze?

ABB Analyze is an add-on for Visual Studio that helps developers review code changes.

- It puts code review tools inside Visual Studio.
- It can run local checks and optional Copilot reviews.
- It can suggest tests and run supported test commands.
- It can connect a code change to an Azure Boards work item.
- It can help show whether the current changes appear to meet the work item's checkpoints.

## 2. Main Features

### 2.1 Local Check

- Runs a quick check of the current Git repository using the bundled QUACK tool.
- Shows the results in Visual Studio.
- Reports findings and test suggestions when they are available.
- Does not ask Copilot to perform the separate AI review.

**Why it helps:** Developers can get a quick first look at the changes before asking for an AI review.

### 2.2 Copilot Code Review

- Runs QUACK's Copilot-backed review flow.
- Uses the selected Copilot model, or QUACK's default model if none is selected.
- Can show a risk level, summary, reasons, affected files, and test suggestions.
- Shows local findings separately from Copilot findings.
- If Copilot is unavailable, the local review can still show results.

**Why it helps:** It gives the developer another review perspective, alongside the local checks.

### 2.3 Choose a Copilot Model

- ABB Analyze can load the Copilot models available to QUACK.
- Choose a model from the Model list when one is available.
- Use **Refresh Models** to load or reload the list.
- If model discovery is not available, ABB Analyze uses the configured default model.

**Why it helps:** The user can select a preferred model without changing the review workflow.

## 3. Review Screens

### 3.1 Overview

- Shows the review summary, risk, and reasons.
- Shows the repository and branch being reviewed.
- Lists affected files.
- Double-click an affected file to open it in Visual Studio.

### 3.2 Findings

- Shows each reported issue in a table.
- Displays severity, source, rule, file/location, and message.
- Double-click a finding with a file location to open the code.

### 3.3 Suggested Tests

- Shows which source file a test suggestion relates to.
- Shows a suggested test command and recommendation.
- Lets the user edit the command, open a related file, or copy the command.
- Provides a Run action for supported test commands.
- Only supported `dotnet test` and `pytest` command forms are allowed to run. Shell chaining and redirection are rejected.
- If a test file already exists but was not included in the staged changes, ABB Analyze can look for it by name to avoid incorrectly calling it a coverage gap.

**Why it helps:** Developers can see a suggested next test and run it without leaving the tool window.

### 3.4 Test Results

- Shows the command that ran and its exit code.
- Shows whether the tests passed, failed, or matched no tests.
- Shows the test output and error output.
- A test command stops if it runs longer than ten minutes.

### 3.5 Raw JSON

- Shows the raw result returned by QUACK.
- Useful when investigating or checking the detailed analysis response.
- The toolbar copy button copies the JSON.

## 4. Helpful Review Actions

### 4.1 Copy Summary

- Creates a short Markdown review summary.
- Includes risk, repository, summary, reasons, and test guidance.
- Copies the text to the clipboard for a pull request or review note.

### 4.2 Generate PR Description

- Creates a Markdown pull-request description from the current review.
- Includes mutation-testing information if mutation testing has been run.
- Copies the description to the clipboard.

### 4.3 Open a File from a Finding

- Double-click a finding or affected file to open it in Visual Studio.
- Suggested-test rows also provide actions to open a file or copy a test command/path.

## 5. Mutation Testing

Mutation testing makes small, artificial changes to code and checks whether tests detect them.

- Run it for the whole solution or for modified files only.
- Review the mutation score and counts for killed, surviving, timed-out, no-coverage, ignored, and compile-error mutations.
- Inspect surviving mutations by file, line, mutator, and description.
- Select a survivor to navigate to its source when possible.
- Choose **Export CSV** to save the score, mutation counts, and surviving-mutant details for Excel or other spreadsheet tools.
- ABB Analyze reads the Stryker.NET JSON report and adds the source line when it can find it.
- Stryker.NET must be available to `dotnet` or installed through the repository's local tool setup.

**How to explain the score:** A stronger score generally means more of the tested code changes were caught by tests. It is a useful test-strength signal, not proof that a feature is correct.

## 6. Azure Boards: Story-to-Code Checkpoints

The Azure Boards view connects a work item to the current code changes.

### 6.1 Connect to Azure DevOps

- Enter an Azure DevOps Personal Access Token (PAT) in the masked field.
- Use an organization-scoped token with only **Work Items (Read)** access and a short expiry.
- ABB Analyze clears the field after connecting.
- The token stays in memory for the current Visual Studio session; it is not saved in settings, source files, or logs.
- The sample organization, project, and work item are `ABB-BCI-PCP`, `PCP`, and `1443229`. These can be changed.

### 6.2 Fetch the Work Item

- Fetches the work item's title and type.
- Shows its description and acceptance criteria, when present.
- For bugs, shows repro steps when present.
- Lets the user review the story before creating checkpoints.

### 6.3 Create Checkpoints Locally

Choose **Local Checkpoint** to make an editable list from the work item text.

- Uses acceptance criteria first.
- If there are no acceptance criteria, uses bug repro steps.
- If neither is present, uses the description.
- This is a simple line-based conversion. It does not understand the meaning of the story.
- Review and edit the list so it contains clear requirements, not background or vague goals.

### 6.4 Draft Checkpoints with Copilot

Choose **Copilot Checkpoint** to ask the selected model to suggest concise, testable checkpoints.

- The suggestions are editable.
- They start as **Not evaluated**.
- Review them before evaluating; Copilot suggestions may need correction.

### 6.5 Load the Current Git Diff

Choose **Refresh diff** to display current saved Git changes.

- Includes staged changes and unstaged changes to tracked files.
- Includes untracked text files.
- Does not include binary untracked files.
- The amount of diff sent to Copilot is limited. Changes left out of the prompt cannot be used as evidence.

### 6.6 Evaluate with Copilot

Choose **Evaluate** to compare the checkpoints with the current diff.

- ABB Analyze asks for confirmation before sending work-item text and changed code to GitHub Copilot.
- Copilot uses the selected model and returns a result and evidence for each checkpoint.
- The GitHub Copilot CLI must be installed and signed in for the current Windows user.
- Copilot tools and built-in MCP servers are disabled for this evaluation. It is asked to use only the information supplied by ABB Analyze.
- The Azure DevOps PAT is not included in the Copilot request.

Each checkpoint can have one of three results:

- **Met:** The changed code appears to support the checkpoint.
- **Not met:** The diff directly conflicts with the checkpoint or shows an implementation that fails it.
- **Inconclusive:** There is not enough reliable information to decide.

For **Met** or **Not met**, Copilot must provide a file name and an exact quote from a line added in the diff. ABB Analyze checks that the file and quote really appear in the diff. If the evidence is missing or cannot be checked, the result becomes **Inconclusive**.

### 6.7 Understand the Overall Gate

- **GATE PASSED:** every checkpoint is Met.
- **GATE FAILED:** at least one checkpoint is Not met.
- **REVIEW REQUIRED:** one or more checkpoints are Inconclusive or have not been evaluated.

If the user edits a checkpoint, fetches another work item, or refreshes the diff, old results are cleared so an earlier pass is not mistaken for a result on the new input.

**Important:** Copilot's result is an aid to review, not proof. Check the cited code and run relevant tests before relying on the gate.

## 7. Completion Checks, Theme, and Cancel

- A green check appears on an Azure Boards action button after that action succeeds.
- Starting the action again clears its old check until the new attempt succeeds.
- Changing the work item, checkpoints, or diff clears checks that no longer apply.
- Azure Boards colors follow the active Visual Studio theme.
- Long-running repository analysis, Copilot checkpoint drafting/evaluation, and mutation testing can be cancelled.
- Test commands have a ten-minute timeout, but are not cancelled through the Cancel button.

## 8. Simple Demo Plan

1. Open the ABB Analyze tool window in Visual Studio.
2. Run **Local Check** and point out the summary, risk, and findings.
3. Run **Copilot Analysis** and show the model selector.
4. Open **Suggested Tests** and show a recommendation and its available actions.
5. Open **Azure Boards** and connect with a read-only PAT.
6. Fetch work item `1443229`.
7. Choose **Local Checkpoint** or **Copilot Checkpoint**, then review the checkpoint list.
8. Choose **Refresh diff** to show the current changes.
9. Choose **Evaluate**, confirm sending the story and code to Copilot, then review the results and evidence.
10. Explain that all checkpoints must be Met for a pass; Not met fails the gate; Inconclusive means a person must review.
11. Optionally show mutation testing and the PR description action.

## 9. What Is Needed

- Windows and a compatible Visual Studio installation with ABB Analyze installed.
- A Git repository open as the active Visual Studio solution.
- An Azure DevOps Services PAT with **Work Items (Read)** access for the Azure Boards workflow.
- The GitHub Copilot CLI installed and signed in for Copilot analysis and checkpoint actions.
- .NET test tools, pytest, or Stryker.NET only for the test or mutation-testing features that use them.

## 10. Current Limits to Mention

- Azure Boards connection is for Azure DevOps Services; Azure DevOps Server/on-premises authentication is not covered.
- Copilot checkpoint evaluation only sees the work-item text, checkpoint list, and bounded Git diff that ABB Analyze sends. It does not inspect the entire repository.
- The checkpoint evaluation does not run tests automatically.
- A checkpoint may remain Inconclusive if the diff does not give enough verifiable evidence.
- AI results need developer review; they do not replace tests or engineering judgement.

## Short Closing Statement

> ABB Analyze puts code review, test guidance, mutation testing, and Azure Boards story checkpoints into Visual Studio. It helps developers connect a work item to changed code and highlights where evidence is clear, missing, or needs human review.
