using System.Xml.Linq;
using Rva.ArchitectureTests.Support;

namespace Rva.ArchitectureTests;

/// <summary>
/// Project-level boundaries: the allowed-reference table in .ai/rules/csharp-monolith.md section 2,
/// plus checks that keep this suite covering every project.
/// </summary>
public class ProjectReferenceTests
{
    private static readonly IReadOnlyList<ProjectInfo> Projects = ProjectGraph.Load();

    [Fact]
    public void Project_references_follow_the_allowed_reference_table()
    {
        var violations = ReferenceRules.Violations(Projects.ToList());

        Assert.True(violations.Count == 0, "Forbidden project references:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void Backend_has_one_host_one_shared_kernel_and_contracts_for_every_module()
    {
        var problems = new List<string>();
        if (Projects.Count(p => p.Kind == ProjectKind.Host) != 1) problems.Add("expected exactly one Rva.Host project");
        if (Projects.Count(p => p.Kind == ProjectKind.SharedKernel) != 1) problems.Add("expected exactly one Rva.SharedKernel project");

        var moduleAreas = Projects.Where(p => p.Kind == ProjectKind.Module).Select(p => p.Area!).ToHashSet();
        var contractAreas = Projects.Where(p => p.Kind == ProjectKind.Contracts).Select(p => p.Area!).ToHashSet();
        problems.AddRange(moduleAreas.Except(contractAreas).Select(a => $"module {a} has no Rva.Modules.{a}.Contracts project"));
        problems.AddRange(contractAreas.Except(moduleAreas).Select(a => $"Rva.Modules.{a}.Contracts has no module implementation"));

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Architecture_tests_reference_every_backend_project()
    {
        // Without a reference the project's assembly is never built into this test run, so its types escape the type rules.
        var testProject = Projects.Single(p => p.Name == "Rva.ArchitectureTests");
        var missing = Projects
            .Where(p => p.Kind is ProjectKind.Host or ProjectKind.SharedKernel or ProjectKind.Module or ProjectKind.Contracts)
            .Select(p => p.Name)
            .Except(testProject.ProjectReferences)
            .ToList();

        Assert.True(missing.Count == 0, "Add a ProjectReference in Rva.ArchitectureTests.csproj for: " + string.Join(", ", missing));
    }

    [Fact]
    public void Solution_includes_every_project()
    {
        var solution = XDocument.Load(Path.Combine(ProjectGraph.RepositoryRoot, "VppAggregator.slnx"));
        var inSolution = solution.Descendants("Project")
            .Select(e => Path.GetFileNameWithoutExtension(((string?)e.Attribute("Path"))?.Replace('\\', '/') ?? ""))
            .ToHashSet(StringComparer.Ordinal);

        var missing = Projects.Select(p => p.Name).Where(n => !inSolution.Contains(n)).ToList();

        Assert.True(missing.Count == 0, "Add to VppAggregator.slnx: " + string.Join(", ", missing));
    }
}
