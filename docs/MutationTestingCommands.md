# Mutation Testing & Copilot QA Summary — Command Reference

This is a quick reference for running mutation testing (Stryker.NET) in this
repo, and for generating a Copilot-powered "missing test case" summary from
surviving mutants.

---

## 1. Prerequisites — one-time setup

### 1.1 Install .NET SDKs

- **.NET Framework 4.7.2** targeting pack — required to build the main VSIX
  project (`ABB.Analyze.VisualStudio.csproj`). Installed via Visual Studio
  workloads, not via `dotnet`.
- **.NET 8+ SDK** — required to build/run the `MutationQaSummaryTool` helper
  (uses `GitHub.Copilot.SDK`, which targets net8.0+).

```powershell
dotnet --list-sdks
```

### 1.2 Install Stryker.NET (mutation testing engine)

```powershell
dotnet tool install -g dotnet-stryker
```

To update later:

```powershell
dotnet tool update -g dotnet-stryker
```

Verify installation:

```powershell
dotnet stryker --version
```

### 1.3 Install GitHub Copilot CLI (required by the Copilot SDK helper)

```powershell
npm install -g @github/copilot
```

Verify installation and sign in:

```powershell
copilot --version
copilot login
```

> The Copilot SDK helper (`MutationQaSummaryTool`) launches the Copilot CLI
> in-process via `GitHub.Copilot.SDK`. You must be signed in
> (`copilot login`) and have an active Copilot subscription before running
> the QA summary feature.

---

## 2. Running mutation testing

Mutation testing is driven from the extension UI (ToolWindows ▸ mutation
panel), but under the hood it calls Stryker directly. The equivalent manual
commands are below.

### 2.1 Run mutation testing on the whole solution

```powershell
cd <path-to-test-project-folder>   # the folder containing the Tests .csproj
dotnet stryker --reporter json
```

### 2.2 Run mutation testing scoped to modified files only (matches the
"Run Mutation Testing (Modified Files)" button)

```powershell
cd <path-to-test-project-folder>
dotnet stryker --reporter json --since:HEAD
```

- `--since:HEAD` lets Stryker use its own git diff to scope mutation testing
  to modified code only.
- The extension auto-detects the target test project by preferring a
  `.csproj` whose path contains `.Tests.` or `Test`; otherwise it picks the
  first `.csproj` found under the repository root.

### 2.3 Locate the mutation report

Stryker writes its JSON report under:

```
<project>/StrykerOutput/<timestamp>/reports/mutation-report.json
```

The extension finds the most recently written file under any
`StrykerOutput` folder whose name contains `mutation` or `report`:

```powershell
Get-ChildItem -Recurse -Filter "*.json" | Where-Object { $_.FullName -match "StrykerOutput" -and ($_.Name -match "mutation|report") } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
```

### 2.4 Re-load the most recent report without re-running Stryker

This is what the extension does when you reopen the mutation panel — it
just re-parses the latest existing report (`MutationService.LoadLatest`).
No separate CLI command is needed; just re-open the tool window.

---

## 3. Generating a Copilot QA summary from surviving mutants

Once mutation testing has produced surviving mutants, you can ask GitHub
Copilot to summarize the missing test cases.

### 3.1 From the extension UI

1. Run mutation testing (section 2).
2. In the Mutation Score panel, click **Summarize QA Impact (Copilot)**.
3. The result appears in the QA summary panel; click **Close** to collapse
   it.

### 3.2 Running the helper tool manually (for testing/debugging)

The helper lives at `Tools/MutationQaSummaryTool` and is published into
`MutationQaSummaryTool/` at the solution root (bundled into the VSIX as
content). It is **not** committed to source control (see `.gitignore`) —
rebuild it with `dotnet publish` whenever you change its source.

**Rebuild/republish the helper after any source change:**

```powershell
cd "C:\project\ABB.Analyzer.Git\ABB.Analyze.VisualStudio"
dotnet publish "Tools\MutationQaSummaryTool\MutationQaSummaryTool.csproj" -c Release -o "MutationQaSummaryTool"
```

**Build only, without publishing (fast inner-loop / compile check):**

```powershell
cd "C:\project\ABB.Analyzer.Git\ABB.Analyze.VisualStudio\Tools\MutationQaSummaryTool"
dotnet build
```

**Prepare a sample mutation-summary JSON payload** (same shape the extension
generates from `MutationSummary.Survivors`):

```powershell
@'
{
  "Repository": "ABB.Analyze.VisualStudio",
  "Scope": "test",
  "Score": 80.0,
  "Total": 10,
  "Killed": 8,
  "Survived": 2,
  "TimedOut": 0,
  "NoCoverage": 0,
  "Survivors": [
	{ "File": "Services/Example.cs", "Line": 42, "Mutator": "EqualityOperator", "Description": "Changed == to !=" },
	{ "File": "Services/Example.cs", "Line": 58, "Mutator": "BooleanLiteral", "Description": "Changed true to false" }
  ]
}
'@ | Out-File -Encoding utf8 "C:\temp\sample-mutation-summary.json"
```

**Run the published helper against the sample payload:**

```powershell
cd "C:\project\ABB.Analyzer.Git\ABB.Analyze.VisualStudio\MutationQaSummaryTool"
.\MutationQaSummaryTool.exe "C:\temp\sample-mutation-summary.json"
```

Exit codes:
- `0` — success, summary text printed to stdout.
- `1` — bad usage / input file missing or unparsable / no survivors.
- `2` — Copilot request failed (not signed in, CLI unreachable, timeout,
  etc.) — see stderr for details.

---

## 4. Troubleshooting

### 4.1 "Stryker.NET could not be started"

```powershell
dotnet tool install -g dotnet-stryker
```

### 4.2 "Bundled MutationQaSummaryTool executable was not found"

The helper hasn't been published into the solution root yet. Run:

```powershell
cd "C:\project\ABB.Analyzer.Git\ABB.Analyze.VisualStudio"
dotnet publish "Tools\MutationQaSummaryTool\MutationQaSummaryTool.csproj" -c Release -o "MutationQaSummaryTool"
```

### 4.3 NuGet restore fails with `401 Unauthorized` for `GitHub.Copilot.SDK`

The repo's default NuGet feed (internal Azure DevOps feed) does not host
this package. The helper project has its own `nuget.config`
(`Tools/MutationQaSummaryTool/nuget.config`) that clears sources and only
uses `nuget.org`, so restoring from that project folder directly should
work:

```powershell
cd "C:\project\ABB.Analyzer.Git\ABB.Analyze.VisualStudio\Tools\MutationQaSummaryTool"
dotnet restore
```

### 4.4 Build fails trying to download the Copilot CLI from GitHub releases

If outbound access to `github.com/github/copilot-cli/releases` is blocked,
the SDK's build target will fail downloading `SHA256SUMS.txt`/the CLI
tarball. Point the helper at the Copilot CLI already installed via npm
instead of letting it download its own copy, by setting
`CopilotCliBinaryPath` in `Tools/MutationQaSummaryTool/MutationQaSummaryTool.csproj`:

```powershell
# Find the installed CLI binary path:
Get-ChildItem "$(npm root -g)\@github\copilot" -Filter "copilot.exe" -Recurse
```

```xml
<CopilotCliBinaryPath>C:\Users\<you>\AppData\Roaming\npm\node_modules\@github\copilot\node_modules\@github\copilot-win32-x64\copilot.exe</CopilotCliBinaryPath>
```

### 4.5 "Copilot request failed" at runtime

Make sure you're signed in:

```powershell
copilot login
```

And confirm the CLI works standalone:

```powershell
copilot --version
```

---

## 5. File map

| Purpose                                             | Path                                               |
|------------------------------------------------------|-----------------------------------------------------|
| Runs Stryker.NET, parses `mutation-report.json`       | `Services/MutationService.cs`                       |
| Mutation data models                                  | `Models/MutationModels.cs`                          |
| CSV export of mutation results                        | `Services/MutationCsvExportService.cs`              |
| Locates the published QA-summary helper exe           | `Services/MutationQaSummaryLocator.cs`              |
| Invokes the helper exe out-of-process                 | `Services/MutationQaSummaryRunner.cs`               |
| net8.0 console helper using GitHub.Copilot.SDK        | `Tools/MutationQaSummaryTool/Program.cs`            |
| Mutation UI (buttons, panels)                          | `ToolWindows/AbbAnalyzeControl.xaml`                |
| Mutation UI event handlers                             | `ToolWindows/AbbAnalyzeControl.xaml.cs`             |
