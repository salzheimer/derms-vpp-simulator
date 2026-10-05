namespace Rva.ArchitectureTests.Support;

/// <summary>
/// The allowed-reference table from .ai/rules/csharp-monolith.md section 2, applied to project files.
/// Checking .csproj files (not compiled assemblies) catches a forbidden reference as soon as it is added,
/// even before any code uses it.
/// </summary>
public static class ReferenceRules
{
    public static IReadOnlyList<string> Violations(IReadOnlyCollection<ProjectInfo> projects)
    {
        var byName = projects.ToDictionary(p => p.Name, StringComparer.Ordinal);
        var violations = new List<string>();

        foreach (var project in projects)
        {
            if (project.Kind == ProjectKind.Unknown)
                violations.Add($"{project.Name}: backend project matches no known role (Rva.Host, Rva.SharedKernel, Rva.Modules.<Area>, Rva.Modules.<Area>.Contracts).");

            foreach (var reference in project.ProjectReferences)
            {
                if (!byName.TryGetValue(reference, out var target))
                {
                    violations.Add($"{project.Name} -> {reference}: referenced project does not exist.");
                    continue;
                }

                var reason = ForbiddenReason(project, target);
                if (reason is not null)
                    violations.Add($"{project.Name} -> {target.Name}: {reason}");
            }

            if (project.Kind is ProjectKind.SharedKernel or ProjectKind.Contracts)
            {
                var allowed = project.Kind == ProjectKind.SharedKernel ? "the .NET base class library" : "Rva.SharedKernel";
                if (project.PackageReferences.Count > 0)
                    violations.Add($"{project.Name}: must not have package references ({string.Join(", ", project.PackageReferences)}); it may depend only on {allowed}.");
                if (project.FrameworkReferences.Count > 0)
                    violations.Add($"{project.Name}: must not have framework references ({string.Join(", ", project.FrameworkReferences)}); it may depend only on {allowed}.");
            }
        }

        return violations;
    }

    private static string? ForbiddenReason(ProjectInfo from, ProjectInfo to) => from.Kind switch
    {
        // Tests may reference anything they check.
        ProjectKind.Test => null,

        ProjectKind.SharedKernel =>
            "Rva.SharedKernel must not reference any project.",

        ProjectKind.Contracts when to.Kind != ProjectKind.SharedKernel =>
            ".Contracts projects may reference Rva.SharedKernel only.",

        ProjectKind.Module => to.Kind switch
        {
            ProjectKind.SharedKernel or ProjectKind.Contracts => null,
            ProjectKind.Module => $"a module must not reference another module's implementation; use {to.Name}{ModuleConventions.ContractsSuffix} instead.",
            ProjectKind.Host => "a module must not reference the host.",
            _ => $"a module may reference only Rva.SharedKernel and .Contracts projects, not {to.Kind}.",
        },

        ProjectKind.Host when to.Kind is ProjectKind.External or ProjectKind.Test or ProjectKind.Unknown =>
            $"the host may reference only backend projects, not {to.Kind}.",

        ProjectKind.External when to.Kind is not (ProjectKind.External or ProjectKind.Test) =>
            "separate processes (Rva.Simulator, Rva.Intelligence) must not reference backend projects; use HTTP or MQTT.",

        _ => null,
    };
}
