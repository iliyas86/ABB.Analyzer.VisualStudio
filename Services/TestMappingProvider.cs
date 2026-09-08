using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ABB.Analyze.VisualStudio.Models;

namespace ABB.Analyze.VisualStudio.Services;

/// <summary>
/// Implementation of ITestMappingProvider using QUACK test guidance.
/// Retrieves and caches test-to-source-file mappings from QUACK analysis.
/// </summary>
internal class TestMappingProvider : ITestMappingProvider
{
    private List<TestCaseInfo> _cachedMappings = new();
    private string _cachedRepository = string.Empty;

    /// <summary>
    /// Get all test cases mapped to a source file.
    /// </summary>
    public async Task<IEnumerable<TestCaseInfo>> GetMappedTestsAsync(string sourceFile, CancellationToken cancellationToken)
    {
        return await GetMappedTestsAsync(new[] { sourceFile }, cancellationToken);
    }

    /// <summary>
    /// Get all test cases mapped to multiple source files.
    /// </summary>
    public async Task<IEnumerable<TestCaseInfo>> GetMappedTestsAsync(IEnumerable<string> sourceFiles, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            if (_cachedMappings.Count == 0)
                return Enumerable.Empty<TestCaseInfo>();

            var sourceFileSet = new HashSet<string>(
                sourceFiles.Select(f => RemoveExtension(Path.GetFileName(f))),
                StringComparer.OrdinalIgnoreCase);

            return _cachedMappings
                .Where(m => sourceFileSet.Contains(RemoveExtension(Path.GetFileName(m.SourceFile))))
                .ToList();
        }, cancellationToken);
    }

    /// <summary>
    /// Get test cases that may be impacted by changes in multiple source files (includes transitive dependencies).
    /// For now, this returns the same as GetMappedTestsAsync; future enhancement could add transitive analysis.
    /// </summary>
    public async Task<IEnumerable<TestCaseInfo>> GetImpactedTestsAsync(IEnumerable<string> sourceFiles, CancellationToken cancellationToken)
    {
        // TODO: Future enhancement - analyze test dependencies to find transitively impacted tests
        return await GetMappedTestsAsync(sourceFiles, cancellationToken);
    }

    /// <summary>
    /// Refresh the mapping cache from QUACK output.
    /// Must be called before each mutation test run to ensure mappings are current.
    /// </summary>
    public async Task RefreshAsync(string repositoryPath, CancellationToken cancellationToken)
    {
        await Task.Run(async () =>
        {
            try
            {
                // Only refresh if repository changed
                if (_cachedRepository == repositoryPath && _cachedMappings.Count > 0)
                    return;

                _cachedMappings.Clear();
                _cachedRepository = repositoryPath;

                // Run QUACK check to get test guidance
                ProcessResult checkResult = await QuackRunner.CheckAsync(repositoryPath, cancellationToken);

                if (checkResult.ExitCode != 0 || string.IsNullOrWhiteSpace(checkResult.Output))
                    return;

                // Deserialize QUACK output
                QuackCheckResult result = JsonServices.Deserialize(checkResult.Output);

                if (result?.TestGuidance == null || result.TestGuidance.Count == 0)
                    return;

                // Convert QUACK TestGuidance to TestCaseInfo
                foreach (var guidance in result.TestGuidance)
                {
                    var testInfo = new TestCaseInfo
                    {
                        SourceFile = guidance.SourceFile,
                        Recommendation = guidance.Recommendation,
                        TestFilter = ExtractTestFilter(guidance.TestOrCommand),
                    };

                    // Parse test project path from test command
                    testInfo.TestProjectPath = ExtractTestProjectPath(guidance.TestOrCommand);
                    testInfo.FullyQualifiedName = ExtractFullyQualifiedName(guidance.TestOrCommand);
                    testInfo.TestClassName = ExtractTestClassName(guidance.TestOrCommand);

                    _cachedMappings.Add(testInfo);
                }
            }
            catch
            {
                // If QUACK fails, cache remains empty but service continues
                _cachedMappings.Clear();
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Extract the test project path from a test command.
    /// Example: "dotnet test C:\Project\Tests.csproj" -> "C:\Project\Tests.csproj"
    /// </summary>
    private static string ExtractTestProjectPath(string testOrCommand)
    {
        if (string.IsNullOrWhiteSpace(testOrCommand))
            return string.Empty;

        var match = Regex.Match(testOrCommand, @"(?:['""])?([^'""\s]*\.csproj)(?:['""])?");
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    /// <summary>
    /// Extract test filter from command (e.g., FullyQualifiedName~TestClass.TestMethod).
    /// </summary>
    private static string ExtractTestFilter(string testOrCommand)
    {
        if (string.IsNullOrWhiteSpace(testOrCommand))
            return string.Empty;

        // Look for --filter parameter
        var match = Regex.Match(testOrCommand, @"--filter\s+[""']?([^""'\s]+)[""']?");
        if (match.Success)
            return match.Groups[1].Value;

        return testOrCommand;
    }

    /// <summary>
    /// Extract fully qualified test name.
    /// Example: "FullyQualifiedName~Namespace.TestClass.TestMethod" -> "Namespace.TestClass.TestMethod"
    /// </summary>
    private static string ExtractFullyQualifiedName(string testOrCommand)
    {
        if (string.IsNullOrWhiteSpace(testOrCommand))
            return string.Empty;

        // Try to extract from FullyQualifiedName filter
        var match = Regex.Match(testOrCommand, @"FullyQualifiedName~([^\s]+)");
        if (match.Success)
            return match.Groups[1].Value;

        // Try to extract from other test specifications
        match = Regex.Match(testOrCommand, @"(?:[\./])?([A-Z][A-Za-z0-9]*(?:\.[A-Z][A-Za-z0-9]*)*\.Test[A-Za-z0-9]*)");
        if (match.Success)
            return match.Groups[1].Value;

        return testOrCommand;
    }

    /// <summary>
    /// Extract test class name (without namespace or method).
    /// Example: "Namespace.CustomerServiceTests.ShouldValidate" -> "CustomerServiceTests"
    /// </summary>
    private static string ExtractTestClassName(string testOrCommand)
    {
        if (string.IsNullOrWhiteSpace(testOrCommand))
            return string.Empty;

        var fqn = ExtractFullyQualifiedName(testOrCommand);
        if (string.IsNullOrEmpty(fqn))
            return string.Empty;

        var parts = fqn.Split('.');
        // Test class is typically the second-to-last part (before method name)
        if (parts.Length >= 2)
            return parts[parts.Length - 2];

        return parts.Length > 0 ? parts[0] : string.Empty;
    }

    /// <summary>
    /// Remove .cs extension from a filename.
    /// </summary>
    private static string RemoveExtension(string fileName)
    {
        if (fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            return fileName.Substring(0, fileName.Length - 3);
        return fileName;
    }
}
