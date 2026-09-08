using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ABB.Analyze.VisualStudio.Models;

namespace ABB.Analyze.VisualStudio.Services;

/// <summary>
/// Provides mapping between source files and the test cases that cover them,
/// derived from QUACK test guidance.
/// </summary>
internal interface ITestMappingProvider
{
    /// <summary>
    /// Get all test cases mapped to a source file.
    /// </summary>
    Task<IEnumerable<TestCaseInfo>> GetMappedTestsAsync(string sourceFile, CancellationToken cancellationToken);

    /// <summary>
    /// Get all test cases mapped to multiple source files.
    /// </summary>
    Task<IEnumerable<TestCaseInfo>> GetMappedTestsAsync(IEnumerable<string> sourceFiles, CancellationToken cancellationToken);

    /// <summary>
    /// Get test cases that may be impacted by changes in multiple source files.
    /// </summary>
    Task<IEnumerable<TestCaseInfo>> GetImpactedTestsAsync(IEnumerable<string> sourceFiles, CancellationToken cancellationToken);

    /// <summary>
    /// Refresh the mapping cache from QUACK output for the given repository.
    /// </summary>
    Task RefreshAsync(string repositoryPath, CancellationToken cancellationToken);
}
