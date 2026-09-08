using ABB.Analyze.VisualStudio.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ABB.Analyze.VisualStudio.Tests;

[TestClass]
public class ExistingTestFileLocatorTests
{
    [TestMethod]
    public void FindExistingTestFile_ReturnsNull_WhenNoTestFileExists()
    {
        using var repo = new TempGitRepository();

        repo.WriteFile("Common/Helpers/FilterHelper.cs", "class FilterHelper { }");
        repo.AddAll();
        repo.Commit("initial");

        string result = ExistingTestFileLocator.FindExistingTestFile(repo.Path, "Common/Helpers/FilterHelper.cs");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void FindExistingTestFile_FindsCommittedUnstagedTestFile_ByConvention()
    {
        // Reproduces the reported bug: FilterHelperTests.cs already exists and is
        // committed (so it has no staged diff), yet FilterHelper.cs is staged as a
        // change. QUACK only looks at staged diffs and reports "no_tests_found" for
        // FilterHelper.cs even though a test already exists. This locator must find
        // the existing test file independently of git staging state.
        using var repo = new TempGitRepository();

        repo.WriteFile("Common/Helpers/FilterHelper.cs", "class FilterHelper { }");
        repo.WriteFile("Common/Helpers/FilterHelperTests.cs", "class FilterHelperTests { }");
        repo.AddAll();
        repo.Commit("initial with test");

        // Simulate an unrelated edit to the source file only.
        repo.WriteFile("Common/Helpers/FilterHelper.cs", "class FilterHelper { void X() {} }");
        repo.Add("Common/Helpers/FilterHelper.cs");

        string result = ExistingTestFileLocator.FindExistingTestFile(repo.Path, "Common/Helpers/FilterHelper.cs");

        Assert.IsNotNull(result);
        StringAssert.EndsWith(result.Replace('\\', '/'), "FilterHelperTests.cs");
    }

    [TestMethod]
    public void FindExistingTestFile_FindsUntrackedTestFile()
    {
        using var repo = new TempGitRepository();

        repo.WriteFile("Foo.cs", "class Foo { }");
        repo.AddAll();
        repo.Commit("initial");

        // Test file exists on disk but was never added/committed.
        repo.WriteFile("FooTests.cs", "class FooTests { }");

        string result = ExistingTestFileLocator.FindExistingTestFile(repo.Path, "Foo.cs");

        Assert.IsNotNull(result);
        StringAssert.EndsWith(result.Replace('\\', '/'), "FooTests.cs");
    }

    [TestMethod]
    public void FindExistingTestFile_ReturnsNull_WhenSourceFileIsItselfATestFile()
    {
        using var repo = new TempGitRepository();

        repo.WriteFile("FooTests.cs", "class FooTests { }");
        repo.AddAll();
        repo.Commit("initial");

        string result = ExistingTestFileLocator.FindExistingTestFile(repo.Path, "FooTests.cs");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void BuildTestCommand_ReturnsRunnableDotnetTestCommand_TargetingContainingProject()
    {
        using var repo = new TempGitRepository();

        repo.WriteFile("Common/Helpers/FilterHelper.cs", "class FilterHelper { }");
        repo.WriteFile("Common/Helpers/FilterHelperTests.cs", "class FilterHelperTests { }");
        repo.WriteFile("Common/Helpers/Tests.csproj", "<Project />");
        repo.AddAll();
        repo.Commit("initial");

        string command = ExistingTestFileLocator.BuildTestCommand(repo.Path, "Common/Helpers/FilterHelperTests.cs");

        Assert.IsFalse(string.IsNullOrWhiteSpace(command));
        StringAssert.Contains(command, "dotnet test");
        StringAssert.Contains(command, "Tests.csproj");
        StringAssert.Contains(command, "FilterHelperTests");
    }

    [TestMethod]
    public void BuildTestCommand_ReturnsEmpty_WhenNoContainingProjectFound()
    {
        using var repo = new TempGitRepository();

        repo.WriteFile("Common/Helpers/FilterHelperTests.cs", "class FilterHelperTests { }");
        repo.AddAll();
        repo.Commit("initial");

        string command = ExistingTestFileLocator.BuildTestCommand(repo.Path, "Common/Helpers/FilterHelperTests.cs");

        Assert.IsTrue(string.IsNullOrWhiteSpace(command));
    }
}
