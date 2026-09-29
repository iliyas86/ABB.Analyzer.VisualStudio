using ABB.Analyze.VisualStudio.Models;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ABB.Analyze.VisualStudio.Services;

// Enriches a Stryker.NET surviving mutant with the offending source line and
// actionable guidance. This is done entirely from data already produced by
// Stryker (mutation-report.json) plus the source file on disk - QUACK does
// not expose a per-mutant "explain" command, so no external AI call is made.
internal static class MutationAnalysisService
{
    public static Task<MutationAnalysisResult> AnalyzeAsync(
        string repository,
        SurvivingMutant mutant,
        CancellationToken cancellationToken)
    {
        return Task.Run(
            () => Analyze(repository, mutant),
            cancellationToken);
    }

    private static MutationAnalysisResult Analyze(
        string repository,
        SurvivingMutant mutant)
    {
        string sourceLine =
            TryReadSourceLine(repository, mutant.File, mutant.Line);

        string baseDescription =
            string.IsNullOrWhiteSpace(mutant.Description)
                ? $"{mutant.Mutator} mutation survived."
                : mutant.Description;

        string description =
            string.IsNullOrWhiteSpace(sourceLine)
                ? baseDescription
                : $"{baseDescription} At {Path.GetFileName(mutant.File)}:{mutant.Line}: `{sourceLine}`";

        return new MutationAnalysisResult
        {
            Description = description
        };
    }

    private static string TryReadSourceLine(
        string repository,
        string relativePath,
        int line)
    {
        if (line <= 0 || string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        try
        {
            string resolvedPath =
                FileNavigationService.ResolveSourcePath(
                    repository,
                    relativePath);

            if (string.IsNullOrWhiteSpace(resolvedPath) ||
                !File.Exists(resolvedPath))
            {
                return string.Empty;
            }

            string[] lines =
                File.ReadAllLines(resolvedPath);

            int index = line - 1;

            return index >= 0 && index < lines.Length
                ? lines[index].Trim()
                : string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}

