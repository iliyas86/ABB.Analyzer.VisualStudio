using System.Linq;
using ABB.Analyze.VisualStudio.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ABB.Analyze.VisualStudio.Tests;

[TestClass]
public class TestFileStagingServiceTests
{
    [TestMethod]
    public void FindUnstagedTestFiles_ReturnsEmpty_WhenTestFileAlreadyStaged()
    {
        using var repo = new TempGitRepository();

        repo.WriteFile("Foo.cs", "class Foo { }");
        repo.AddAll();
        repo.Commit("initial");

        repo.WriteFile("Foo.cs", "class Foo { void X() {} }");
        repo.WriteFile("FooTests.cs", "class FooTests { }");
        repo.AddAll();

        var result = TestFileStagingService.FindUnstagedTestFiles(repo.Path);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void FindUnstagedTestFiles_ReturnsUntrackedMatchingTestFile()
    {
        using var repo = new TempGitRepository();

        repo.WriteFile("Foo.cs", "class Foo { }");
        repo.AddAll();
        repo.Commit("initial");

        repo.WriteFile("Foo.cs", "class Foo { void X() {} }");
        repo.Add("Foo.cs");

        repo.WriteFile("FooTests.cs", "class FooTests { }");

        var result = TestFileStagingService.FindUnstagedTestFiles(repo.Path);

        Assert.AreEqual(1, result.Count);
        StringAssert.EndsWith(result.Single().Replace('\\', '/'), "FooTests.cs");
    }

    [TestMethod]
    public void FindUnstagedTestFiles_ReturnsEmpty_WhenNoSourceFilesAreStaged()
    {
        using var repo = new TempGitRepository();

        repo.WriteFile("Foo.cs", "class Foo { }");
        repo.WriteFile("FooTests.cs", "class FooTests { }");
        repo.AddAll();
        repo.Commit("initial");

        var result = TestFileStagingService.FindUnstagedTestFiles(repo.Path);

        Assert.AreEqual(0, result.Count);
    }
}
