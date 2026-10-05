using Assembly = System.Reflection.Assembly;
using AssemblyName = System.Reflection.AssemblyName;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;

namespace Rva.ArchitectureTests.Support;

/// <summary>
/// The compiled backend assemblies, discovered from the project files so new modules are checked automatically.
/// Each assembly is available because this test project references every backend project.
/// </summary>
public static class BackendAssemblies
{
    public static IReadOnlyList<ProjectInfo> Projects { get; } =
        ProjectGraph.Load().Where(p => p.Kind is ProjectKind.Host or ProjectKind.SharedKernel or ProjectKind.Module or ProjectKind.Contracts).ToList();

    public static Assembly Host => Load(OfKind(ProjectKind.Host).Single());
    public static Assembly SharedKernel => Load(OfKind(ProjectKind.SharedKernel).Single());
    public static IReadOnlyList<Assembly> Modules => OfKind(ProjectKind.Module).Select(Load).ToList();
    public static IReadOnlyList<Assembly> Contracts => OfKind(ProjectKind.Contracts).Select(Load).ToList();
    public static IReadOnlyList<Assembly> All => Projects.Select(Load).ToList();

    public static Architecture Architecture { get; } = new ArchLoader().LoadAssemblies(All.ToArray()).Build();

    public static Assembly Load(ProjectInfo project) => Assembly.Load(new AssemblyName(project.Name));

    private static IEnumerable<ProjectInfo> OfKind(ProjectKind kind) => Projects.Where(p => p.Kind == kind);
}
