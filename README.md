# ABB AI Code Analyzer + QUACK v0.8

ABB Analyze is a Visual Studio extension that brings code-quality analysis, AI-assisted review, test guidance, pull-request description generation, and mutation-testing workflows directly into the IDE.

The extension uses the **QUACK** analysis engine. A tested, self-contained `quack.exe` is bundled inside the VSIX, so end users do not need to install Python, create a virtual environment, modify `PATH`, or configure `QUACK_EXE`.

> **Internal project:** Update the repository URL, support contact, publisher, license, screenshots, and release policy before publishing outside the approved private environment.

## Features

- Run a fast local repository check
- Analyze changes using an available Copilot-backed model
- Discover available models dynamically and select a preferred model
- Display repository risk, summary, reasons, and affected files
- Review structured findings with severity, source, rule, location, and message
- Generate suggested tests and execute supported test commands
- Display test output inside the tool window
- Generate pull-request descriptions
- Run Stryker.NET mutation testing and review surviving mutants
- Inspect and copy raw JSON returned by QUACK
- Navigate from findings and files to the relevant source location
- Follow the active Visual Studio light or dark theme
- Cancel supported long-running operations

## Architecture

```text
Visual Studio
    |
    +-- ABB Analyze VSIX
          |
          +-- WPF tool window
          +-- QUACK process runner
          +-- Model discovery and selection
          +-- Test and mutation orchestration
          |
          +-- Quack/
                +-- quack.exe
```

The extension starts the bundled executable by absolute path:

```text
<extension-install-directory>\Quack\quack.exe
```

The repository being analyzed remains the process working directory. This allows QUACK to inspect the active solution repository while avoiding machine-level environment changes.

## Requirements

### End users

- A supported Windows version
- A compatible Visual Studio installation
- Access to the provider and authentication mechanism required by QUACK
- .NET SDK and test tooling required by the repository being analyzed
- Stryker.NET only when mutation testing is used and the repository does not provide it through a local tool manifest

Python is not required when the bundled `quack.exe` is self-contained.

### Extension developers

- Visual Studio with the **Visual Studio extension development** workload
- The target .NET Framework developer pack used by the project, currently `net472`
- Git
- A tested self-contained build of `quack.exe`

Microsoft documents the Visual Studio extension development workload as the required tooling for building extensions. The VSIX project and manifest define the assets packaged for deployment.

## Repository layout


```text
ABB.Analyze.VisualStudio/
|-- Assets/
|-- Models/
|-- Services/
|   |-- QuackLocator.cs
|   +-- QuackRunner.cs
|-- ToolWindows/
|   |-- AbbAnalyzeControl.xaml
|   +-- AbbAnalyzeControl.xaml.cs
|-- Quack/
|   +-- quack.exe
|-- extension.vsixmanifest
|-- ABB.Analyze.VisualStudio.csproj
+-- README.md
```

The exact structure may vary as the project evolves. The important runtime asset is:

```text
Quack\quack.exe
```
