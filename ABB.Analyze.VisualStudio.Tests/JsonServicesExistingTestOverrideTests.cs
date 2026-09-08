using System.Linq;
using ABB.Analyze.VisualStudio.Models;
using ABB.Analyze.VisualStudio.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ABB.Analyze.VisualStudio.Tests;

[TestClass]
public class JsonServicesExistingTestOverrideTests
{
    [TestMethod]
    public void Build_OverridesCoverageGap_WhenTestFileExistsButHasNoStagedDiff()
    {
        // Reproduces the reported issue: QUACK reports "no_tests_found" (displayed as
        // "Coverage gap") for FilterHelper.cs even though FilterHelperTests.cs already
        // exists, because that test file is committed and unchanged (no staged diff).
        using var repo = new TempGitRepository();

        repo.WriteFile("Common/Helpers/FilterHelper.cs", "class FilterHelper { }");
        repo.WriteFile("Common/Helpers/FilterHelperTests.cs", "class FilterHelperTests { }");
        repo.WriteFile("Common/Helpers/Tests.csproj", "<Project />");
        repo.AddAll();
        repo.Commit("initial with test");

        repo.WriteFile("Common/Helpers/FilterHelper.cs", "class FilterHelper { void X() {} }");
        repo.Add("Common/Helpers/FilterHelper.cs");

        var quackResult = new QuackCheckResult();
        quackResult.TestGuidance.Add(new QuackTestGuidance
        {
            SourceFile = "Common/Helpers/FilterHelper.cs",
            TestOrCommand = string.Empty,
            Status = "no_tests_found",
            Recommendation = "Add or map tests for the changed source file."
        });

        DashboardModel model = JsonServices.Build(quackResult, "master", "{}", repo.Path);

        TestRow row = model.Tests.Single();

        Assert.AreEqual("existing_test_found", row.Status);
        Assert.AreEqual("Test exists (unstaged diff)", row.DisplayStatus);
        StringAssert.Contains(row.Recommendation, "FilterHelperTests.cs");

        // The Command column must not be left blank: QUACK never saw the existing
        // test file, so a runnable command must be synthesized from it.
        Assert.IsFalse(string.IsNullOrWhiteSpace(row.TestOrCommand), "Command column should not be empty when an existing test file is found.");
        StringAssert.Contains(row.TestOrCommand, "FilterHelperTests");
        StringAssert.Contains(row.TestOrCommand, "Tests.csproj");
        Assert.IsTrue(row.CanRun);
    }

    [TestMethod]
    public void Build_KeepsCoverageGap_WhenNoTestFileExistsAtAll()
    {
        using var repo = new TempGitRepository();

        repo.WriteFile("Common/Helpers/FilterHelper.cs", "class FilterHelper { }");
        repo.AddAll();
        repo.Commit("initial");

        repo.WriteFile("Common/Helpers/FilterHelper.cs", "class FilterHelper { void X() {} }");
        repo.Add("Common/Helpers/FilterHelper.cs");

        var quackResult = new QuackCheckResult();
        quackResult.TestGuidance.Add(new QuackTestGuidance
        {
            SourceFile = "Common/Helpers/FilterHelper.cs",
            TestOrCommand = string.Empty,
            Status = "no_tests_found",
            Recommendation = "Add or map tests for the changed source file."
        });

        DashboardModel model = JsonServices.Build(quackResult, "master", "{}", repo.Path);

        TestRow row = model.Tests.Single();

        Assert.AreEqual("no_tests_found", row.Status);
        Assert.AreEqual("Coverage gap", row.DisplayStatus);
    }
}
