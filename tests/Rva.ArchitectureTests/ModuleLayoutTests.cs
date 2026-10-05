using Rva.ArchitectureTests.Support;

namespace Rva.ArchitectureTests;

/// <summary>Vertical slicing inside modules (.ai/rules/csharp-monolith.md section 4): no technical-layer folders.</summary>
public class ModuleLayoutTests
{
    private static readonly string[] LayerFolders = ["Controllers", "Services", "Repositories"];

    [Fact]
    public void Modules_have_no_technical_layer_folders()
    {
        var problems = ProjectGraph.Load()
            .Where(p => p.Kind is ProjectKind.Module or ProjectKind.Contracts)
            .SelectMany(p => Directory.EnumerateDirectories(p.Directory, "*", SearchOption.AllDirectories)
                .Where(d => !d.Split(Path.DirectorySeparatorChar).Any(s => s is "bin" or "obj"))
                .Where(d => LayerFolders.Contains(Path.GetFileName(d)))
                .Select(d => $"{Path.GetRelativePath(ProjectGraph.RepositoryRoot, d)}: organize by feature (Features/<FeatureName>/), not by layer"))
            .ToList();

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Module_namespaces_have_no_technical_layer_segments()
    {
        var problems = BackendAssemblies.Modules.Concat(BackendAssemblies.Contracts)
            .SelectMany(a => a.GetTypes())
            .Where(t => t.Namespace is not null && t.Namespace.Split('.').Any(LayerFolders.Contains))
            .Select(t => $"{t.FullName}: namespace uses a technical-layer segment; organize by feature")
            .ToList();

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
