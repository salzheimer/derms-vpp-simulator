using System.Xml.Linq;

namespace Rva.ArchitectureTests.Support;

/// <summary>Role of a project in the solution, derived from its name and location.</summary>
public enum ProjectKind
{
    Host,
    SharedKernel,
    Module,
    Contracts,
    /// <summary>A separate process under src/ that is not part of the backend (e.g. Rva.Simulator).</summary>
    External,
    Test,
    /// <summary>A backend project that matches no known role; always a violation until it is classified.</summary>
    Unknown,
}

public sealed record ProjectInfo(
    string Name,
    ProjectKind Kind,
    string Directory,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences,
    IReadOnlyList<string> FrameworkReferences)
{
    /// <summary>The module area for Module and Contracts projects, e.g. "GridTopology"; otherwise null.</summary>
    public string? Area => Kind switch
    {
        ProjectKind.Module => Name[ModuleConventions.Prefix.Length..],
        ProjectKind.Contracts => Name[ModuleConventions.Prefix.Length..^ModuleConventions.ContractsSuffix.Length],
        _ => null,
    };
}

public static class ModuleConventions
{
    public const string HostName = "Rva.Host";
    public const string SharedKernelName = "Rva.SharedKernel";
    public const string Prefix = "Rva.Modules.";
    public const string ContractsSuffix = ".Contracts";

    public static ProjectKind Classify(string name, string repoRelativeDirectory)
    {
        var dir = repoRelativeDirectory.Replace('\\', '/');

        if (dir.StartsWith("tests/", StringComparison.Ordinal))
            return ProjectKind.Test;

        if (!dir.StartsWith("src/Rva.Backend/", StringComparison.Ordinal))
            return ProjectKind.External;

        if (name == HostName) return ProjectKind.Host;
        if (name == SharedKernelName) return ProjectKind.SharedKernel;

        if (name.StartsWith(Prefix, StringComparison.Ordinal))
        {
            var rest = name[Prefix.Length..];
            if (rest.EndsWith(ContractsSuffix, StringComparison.Ordinal) && !rest[..^ContractsSuffix.Length].Contains('.'))
                return ProjectKind.Contracts;
            if (rest.Length > 0 && !rest.Contains('.'))
                return ProjectKind.Module;
        }

        return ProjectKind.Unknown;
    }
}

/// <summary>Reads every .csproj under src/ and tests/ and resolves project references to project names.</summary>
public static class ProjectGraph
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static IReadOnlyList<ProjectInfo> Load()
    {
        var files = new[] { "src", "tests" }
            .Select(d => Path.Combine(RepositoryRoot, d))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.csproj", SearchOption.AllDirectories))
            .Where(f => !IsBuildOutput(f))
            .Select(Path.GetFullPath)
            .ToList();

        var nameByPath = files.ToDictionary(f => f, f => Path.GetFileNameWithoutExtension(f), StringComparer.Ordinal);

        return files.Select(file =>
        {
            var doc = XDocument.Load(file);
            var dir = Path.GetDirectoryName(file)!;
            var name = nameByPath[file];

            var projectRefs = doc.Descendants("ProjectReference")
                .Select(e => (string?)e.Attribute("Include"))
                .OfType<string>()
                .Select(include => Path.GetFullPath(Path.Combine(dir, include.Replace('\\', Path.DirectorySeparatorChar))))
                .Select(target => nameByPath.TryGetValue(target, out var n) ? n : $"<missing:{Path.GetFileName(target)}>")
                .ToList();

            var packageRefs = doc.Descendants("PackageReference")
                .Select(e => (string?)e.Attribute("Include"))
                .OfType<string>()
                .ToList();

            var frameworkRefs = doc.Descendants("FrameworkReference")
                .Select(e => (string?)e.Attribute("Include"))
                .OfType<string>()
                .ToList();

            var relativeDir = Path.GetRelativePath(RepositoryRoot, dir).Replace('\\', '/') + "/";
            return new ProjectInfo(name, ModuleConventions.Classify(name, relativeDir), dir, projectRefs, packageRefs, frameworkRefs);
        }).ToList();
    }

    private static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar).Any(segment => segment is "bin" or "obj");

    private static string FindRepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "VppAggregator.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException("Could not find VppAggregator.slnx above " + AppContext.BaseDirectory);
    }
}
