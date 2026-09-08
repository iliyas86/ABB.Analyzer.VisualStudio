using System.Linq;
using ABB.Analyze.VisualStudio.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ABB.Analyze.VisualStudio.Tests;

[TestClass]
public class GitCommandServiceTests
{
    [TestMethod]
    public void GetStagedFiles_ReturnsOnlyStagedFiles()
    {
        using var repo = new TempGitRepository();

        repo.WriteFile("Foo.cs", "class Foo { }");
        repo.WriteFile("Bar.cs", "class Bar { }");
        repo.AddAll();
        repo.Commit("initial");

        repo.WriteFile("Foo.cs", "class Foo { void X() {} }");
        repo.Add("Foo.cs");

        repo.WriteFile("Bar.cs", "class Bar { void Y() {} }"); // modified, not staged

        string[] staged = GitCommandService.GetStagedFiles(repo.Path);

        CollectionAssert.Contains(staged, "Foo.cs");
        CollectionAssert.DoesNotContain(staged, "Bar.cs");
    }

    [TestMethod]
    public void GetModifiedFiles_IncludesStagedAndUnstagedChanges()
    {
        using var repo = new TempGitRepository();

        repo.WriteFile("Foo.cs", "class Foo { }");
        repo.WriteFile("Bar.cs", "class Bar { }");
        repo.AddAll();
        repo.Commit("initial");

        repo.WriteFile("Foo.cs", "class Foo { void X() {} }");
        repo.Add("Foo.cs");

        repo.WriteFile("Bar.cs", "class Bar { void Y() {} }"); // modified, not staged

        string[] modified = GitCommandService.GetModifiedFiles(repo.Path);

        CollectionAssert.Contains(modified, "Foo.cs");
        CollectionAssert.Contains(modified, "Bar.cs");
    }

    [TestMethod]
    public void GetUntrackedFiles_ReturnsNewFilesNotYetAdded()
    {
        using var repo = new TempGitRepository();

        repo.WriteFile("Foo.cs", "class Foo { }");
        repo.AddAll();
        repo.Commit("initial");

        repo.WriteFile("New.cs", "class New { }");

        string[] untracked = GitCommandService.GetUntrackedFiles(repo.Path);

        CollectionAssert.Contains(untracked, "New.cs");
    }

    [TestMethod]
    public void StageFile_MovesFileIntoStagedSet()
    {
        using var repo = new TempGitRepository();

        repo.WriteFile("Foo.cs", "class Foo { }");
        repo.AddAll();
        repo.Commit("initial");

        repo.WriteFile("Foo.cs", "class Foo { void X() {} }");

        bool staged = GitCommandService.StageFile(repo.Path, "Foo.cs");

        Assert.IsTrue(staged);
        CollectionAssert.Contains(GitCommandService.GetStagedFiles(repo.Path), "Foo.cs");
    }

    [TestMethod]
    public void GetAllTrackedFiles_ReturnsCommittedFiles()
    {
        using var repo = new TempGitRepository();

        repo.WriteFile("Foo.cs", "class Foo { }");
        repo.AddAll();
        repo.Commit("initial");

        string[] tracked = GitCommandService.GetAllTrackedFiles(repo.Path);

        CollectionAssert.Contains(tracked, "Foo.cs");
    }
}
