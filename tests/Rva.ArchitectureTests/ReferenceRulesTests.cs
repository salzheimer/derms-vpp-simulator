using Rva.ArchitectureTests.Support;

namespace Rva.ArchitectureTests;

/// <summary>
/// Proves the reference rules reject each forbidden shape, so a passing ProjectReferenceTests run means
/// "no violations" rather than "the rule checks nothing". Uses synthetic project graphs.
/// </summary>
public class ReferenceRulesTests
{
    private static ProjectInfo Project(string name, string repoDir, params string[] references) =>
        new(name, ModuleConventions.Classify(name, repoDir), repoDir, references, [], []);

    private static List<ProjectInfo> ValidBaseline() =>
    [
        Project("Rva.SharedKernel", "src/Rva.Backend/Rva.SharedKernel/"),
        Project("Rva.Modules.A.Contracts", "src/Rva.Backend/Modules/Rva.Modules.A.Contracts/", "Rva.SharedKernel"),
        Project("Rva.Modules.B.Contracts", "src/Rva.Backend/Modules/Rva.Modules.B.Contracts/", "Rva.SharedKernel"),
        Project("Rva.Modules.A", "src/Rva.Backend/Modules/Rva.Modules.A/", "Rva.SharedKernel", "Rva.Modules.A.Contracts"),
        Project("Rva.Modules.B", "src/Rva.Backend/Modules/Rva.Modules.B/", "Rva.SharedKernel", "Rva.Modules.B.Contracts", "Rva.Modules.A.Contracts"),
        Project("Rva.Host", "src/Rva.Backend/Rva.Host/", "Rva.SharedKernel", "Rva.Modules.A", "Rva.Modules.A.Contracts", "Rva.Modules.B", "Rva.Modules.B.Contracts"),
        Project("Rva.Simulator", "src/Rva.Simulator/"),
    ];

    private static IReadOnlyList<string> ViolationsWith(string projectName, params string[] extraReferences)
    {
        var graph = ValidBaseline();
        var index = graph.FindIndex(p => p.Name == projectName);
        graph[index] = graph[index] with { ProjectReferences = graph[index].ProjectReferences.Concat(extraReferences).ToList() };
        return ReferenceRules.Violations(graph);
    }

    [Fact]
    public void Valid_graph_has_no_violations() =>
        Assert.Empty(ReferenceRules.Violations(ValidBaseline()));

    [Fact]
    public void Module_referencing_another_module_implementation_is_rejected() =>
        Assert.Contains(ViolationsWith("Rva.Modules.B", "Rva.Modules.A"), v => v.StartsWith("Rva.Modules.B -> Rva.Modules.A:"));

    [Fact]
    public void Module_referencing_the_host_is_rejected() =>
        Assert.Contains(ViolationsWith("Rva.Modules.A", "Rva.Host"), v => v.StartsWith("Rva.Modules.A -> Rva.Host:"));

    [Fact]
    public void Contracts_referencing_a_module_implementation_is_rejected() =>
        Assert.Contains(ViolationsWith("Rva.Modules.A.Contracts", "Rva.Modules.B"), v => v.StartsWith("Rva.Modules.A.Contracts -> Rva.Modules.B:"));

    [Fact]
    public void Contracts_referencing_other_contracts_is_rejected() =>
        Assert.Contains(ViolationsWith("Rva.Modules.A.Contracts", "Rva.Modules.B.Contracts"), v => v.StartsWith("Rva.Modules.A.Contracts -> Rva.Modules.B.Contracts:"));

    [Fact]
    public void Shared_kernel_referencing_any_project_is_rejected() =>
        Assert.Contains(ViolationsWith("Rva.SharedKernel", "Rva.Modules.A.Contracts"), v => v.StartsWith("Rva.SharedKernel -> Rva.Modules.A.Contracts:"));

    [Fact]
    public void Simulator_referencing_a_backend_project_is_rejected() =>
        Assert.Contains(ViolationsWith("Rva.Simulator", "Rva.Modules.A.Contracts"), v => v.StartsWith("Rva.Simulator -> Rva.Modules.A.Contracts:"));

    [Fact]
    public void Package_reference_in_shared_kernel_or_contracts_is_rejected()
    {
        var graph = ValidBaseline();
        var kernel = graph.FindIndex(p => p.Name == "Rva.SharedKernel");
        var contracts = graph.FindIndex(p => p.Name == "Rva.Modules.A.Contracts");
        graph[kernel] = graph[kernel] with { PackageReferences = ["Newtonsoft.Json"] };
        graph[contracts] = graph[contracts] with { PackageReferences = ["Microsoft.EntityFrameworkCore"] };

        var violations = ReferenceRules.Violations(graph);

        Assert.Contains(violations, v => v.StartsWith("Rva.SharedKernel: must not have package references"));
        Assert.Contains(violations, v => v.StartsWith("Rva.Modules.A.Contracts: must not have package references"));
    }

    [Fact]
    public void Serilog_package_in_a_module_is_rejected()
    {
        var graph = ValidBaseline();
        var module = graph.FindIndex(p => p.Name == "Rva.Modules.A");
        var host = graph.FindIndex(p => p.Name == "Rva.Host");
        graph[module] = graph[module] with { PackageReferences = ["Serilog.Sinks.Console"] };
        graph[host] = graph[host] with { PackageReferences = ["Serilog.AspNetCore"] };

        var violations = ReferenceRules.Violations(graph);

        Assert.Contains(violations, v => v.StartsWith("Rva.Modules.A: must not reference Serilog"));
        Assert.DoesNotContain(violations, v => v.StartsWith("Rva.Host:"));
    }

    [Fact]
    public void Framework_reference_in_contracts_is_rejected()
    {
        var graph = ValidBaseline();
        var contracts = graph.FindIndex(p => p.Name == "Rva.Modules.A.Contracts");
        graph[contracts] = graph[contracts] with { FrameworkReferences = ["Microsoft.AspNetCore.App"] };

        Assert.Contains(ReferenceRules.Violations(graph), v => v.StartsWith("Rva.Modules.A.Contracts: must not have framework references"));
    }

    [Fact]
    public void Unclassified_backend_project_is_rejected()
    {
        var graph = ValidBaseline();
        graph.Add(Project("Rva.Backend.Utilities", "src/Rva.Backend/Rva.Backend.Utilities/"));

        Assert.Contains(ReferenceRules.Violations(graph), v => v.StartsWith("Rva.Backend.Utilities: backend project matches no known role"));
    }
}
