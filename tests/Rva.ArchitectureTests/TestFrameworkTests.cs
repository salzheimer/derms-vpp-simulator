using System.Text.Json;
using Rva.ArchitectureTests.Support;

namespace Rva.ArchitectureTests;

/// <summary>
/// The test framework standard (.ai/rules/testing.md) on the real repository: xUnit v3 on Microsoft.Testing.Platform,
/// with every package version pinned in the root Directory.Packages.props.
/// </summary>
public class TestFrameworkTests
{
    [Fact]
    public void Build_files_use_only_the_approved_test_frameworks()
    {
        var violations = TestFrameworkRules.Violations(BuildFiles.Load());

        Assert.True(violations.Count == 0, "Test framework standard violations:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Every_test_project_in_the_graph_is_checked()
    {
        // Guards the loader: if it stopped finding the test projects, the rule above would pass vacuously.
        var loaded = BuildFiles.Load().Where(f => f.IsProject).Select(f => f.RepoPath).ToHashSet();
        var testProjects = ProjectGraph.Load()
            .Where(p => p.Kind == ProjectKind.Test)
            .Select(p => Path.GetRelativePath(ProjectGraph.RepositoryRoot, Path.Combine(p.Directory, p.Name + ".csproj")).Replace('\\', '/'))
            .ToList();

        Assert.NotEmpty(testProjects);
        Assert.All(testProjects, p => Assert.Contains(p, loaded));
    }

    [Fact]
    public void Dotnet_test_runs_on_Microsoft_Testing_Platform()
    {
        var path = Path.Combine(ProjectGraph.RepositoryRoot, "global.json");
        Assert.True(File.Exists(path), $"global.json is missing at the repo root; it opts dotnet test into Microsoft.Testing.Platform ({TestFrameworkRules.Rule}).");

        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var runner = json.RootElement.TryGetProperty("test", out var test) && test.TryGetProperty("runner", out var r) ? r.GetString() : null;

        Assert.True(runner == "Microsoft.Testing.Platform",
            $"global.json must set \"test\": {{ \"runner\": \"Microsoft.Testing.Platform\" }} (found: {runner ?? "none"}) ({TestFrameworkRules.Rule}).");
    }
}
