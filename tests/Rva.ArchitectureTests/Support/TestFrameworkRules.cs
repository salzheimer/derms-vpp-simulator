using System.Xml.Linq;

namespace Rva.ArchitectureTests.Support;

/// <summary>A package named in an MSBuild file, with whether that file also pins a version for it.</summary>
public sealed record PackageUse(string Package, bool HasVersion = false, bool HasVersionOverride = false);

/// <summary>
/// The package-related content of one MSBuild file (.csproj, .props, .targets): what it references, what versions
/// it declares, and whether it switches off central package management or marks itself a test project.
/// </summary>
public sealed record BuildFile(
    string RepoPath,
    IReadOnlyList<PackageUse> PackageReferences,
    IReadOnlyList<string> PackageVersions,
    bool DisablesCentralVersions = false,
    bool DeclaresTestProject = false)
{
    public bool IsProject => RepoPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
    public string FileName => RepoPath[(RepoPath.LastIndexOf('/') + 1)..];
}

/// <summary>
/// The test framework standard in .ai/rules/testing.md, applied to every MSBuild file in the repo. Every package
/// version lives in the root Directory.Packages.props, so a test framework can only enter the build through it;
/// these rules keep that the only way in, and keep the wrong frameworks out of it.
/// Pure function over parsed files, so TestFrameworkRulesTests can prove each rule rejects its forbidden shape.
/// </summary>
public static class TestFrameworkRules
{
    public const string Rule = ".ai/rules/testing.md";
    public const string CentralPackagesFile = "Directory.Packages.props";
    public const string Framework = "xunit.v3";

    private const string UseFramework = $"use {Framework}";
    private const string UseArchUnitV3 = "use TngTech.ArchUnitNET.xUnitV3";
    private const string NotNeeded = $"remove it; {Framework} runs on Microsoft.Testing.Platform, which needs no VSTest runner or test SDK";

    // Forbidden package (exact name, or prefix when it ends in '.' or '*') -> what to do instead.
    private static readonly (string Pattern, string Fix)[] Forbidden =
    [
        ("xunit", UseFramework),
        ("xunit.core", UseFramework),
        ("xunit.assert", UseFramework),
        ("xunit.abstractions", UseFramework),
        ("xunit.extensibility.", UseFramework),
        ("xunit.runner.", NotNeeded),
        ("Microsoft.NET.Test.Sdk", NotNeeded),
        ("NUnit*", UseFramework),
        ("MSTest*", UseFramework),
        ("TUnit*", UseFramework),
        ("coverlet.collector", "use Microsoft.Testing.Extensions.CodeCoverage"),
        ("TngTech.ArchUnitNET.xUnit", UseArchUnitV3),
        ("TngTech.ArchUnitNET.NUnit", UseArchUnitV3),
        ("TngTech.ArchUnitNET.MSTestV2", UseArchUnitV3),
    ];

    // Packages that make a project a test project, whichever framework they belong to.
    private static readonly string[] TestPackagePrefixes =
        ["xunit", "NUnit", "MSTest", "TUnit", "Microsoft.NET.Test.Sdk", "Microsoft.Testing.", "TngTech.ArchUnitNET.", "Microsoft.AspNetCore.Mvc.Testing"];

    public static IReadOnlyList<string> Violations(IReadOnlyCollection<BuildFile> files)
    {
        var violations = new List<string>();

        var centralFiles = files.Where(f => f.FileName.Equals(CentralPackagesFile, StringComparison.OrdinalIgnoreCase)).ToList();
        if (centralFiles.Count != 1 || centralFiles[0].RepoPath != CentralPackagesFile)
            violations.Add($"expected exactly one {CentralPackagesFile}, at the repo root; found: {(centralFiles.Count == 0 ? "none" : string.Join(", ", centralFiles.Select(f => f.RepoPath)))}. A nested one replaces the root file for its whole folder ({Rule}).");

        foreach (var file in files)
        {
            if (file.DisablesCentralVersions)
                violations.Add($"{file.RepoPath}: sets ManagePackageVersionsCentrally to false; all package versions come from the root {CentralPackagesFile} ({Rule}).");

            if (file.PackageVersions.Count > 0 && file.RepoPath != CentralPackagesFile)
                violations.Add($"{file.RepoPath}: declares PackageVersion items; only the root {CentralPackagesFile} may ({Rule}).");

            foreach (var use in file.PackageReferences.Where(u => u.HasVersion || u.HasVersionOverride))
                violations.Add($"{file.RepoPath}: PackageReference {use.Package} sets {(use.HasVersion ? "Version" : "VersionOverride")}; pin versions only in the root {CentralPackagesFile} ({Rule}).");

            foreach (var package in file.PackageVersions.Concat(file.PackageReferences.Select(u => u.Package)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var fix = ForbiddenFix(package);
                if (fix is not null)
                    violations.Add($"{file.RepoPath}: {package} is not an approved test package; {fix} ({Rule}).");
            }

            if (file.IsProject && IsTestProject(file))
            {
                if (!file.RepoPath.StartsWith("tests/", StringComparison.Ordinal))
                    violations.Add($"{file.RepoPath}: test projects live under tests/ ({Rule}).");
                if (!file.PackageReferences.Any(u => u.Package.Equals(Framework, StringComparison.OrdinalIgnoreCase)))
                    violations.Add($"{file.RepoPath}: test project does not reference {Framework}; every .NET test project uses xUnit v3 on Microsoft.Testing.Platform ({Rule}).");
            }
        }

        return violations;
    }

    private static bool IsTestProject(BuildFile file) =>
        file.DeclaresTestProject
        || file.PackageReferences.Any(u => TestPackagePrefixes.Any(p => u.Package.StartsWith(p, StringComparison.OrdinalIgnoreCase)));

    private static string? ForbiddenFix(string package)
    {
        foreach (var (pattern, fix) in Forbidden)
        {
            var matches = pattern switch
            {
                _ when pattern.EndsWith('*') => package.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase),
                _ when pattern.EndsWith('.') => package.StartsWith(pattern, StringComparison.OrdinalIgnoreCase),
                _ => package.Equals(pattern, StringComparison.OrdinalIgnoreCase),
            };
            if (matches) return fix;
        }
        return null;
    }
}

/// <summary>Reads every .csproj, .props and .targets file in the repo (outside build output) into <see cref="BuildFile"/>s.</summary>
public static class BuildFiles
{
    private static readonly string[] Extensions = [".csproj", ".props", ".targets"];
    private static readonly string[] SkippedFolders = ["bin", "obj", ".git", "node_modules"];

    public static IReadOnlyList<BuildFile> Load()
    {
        var root = ProjectGraph.RepositoryRoot;
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .Where(f => !Path.GetRelativePath(root, f).Split(Path.DirectorySeparatorChar).Any(s => SkippedFolders.Contains(s)))
            .Select(f => Parse(Path.GetRelativePath(root, f).Replace('\\', '/'), XDocument.Load(f)))
            .ToList();
    }

    public static BuildFile Parse(string repoPath, XDocument doc)
    {
        // GlobalPackageReference adds a package to every project, so it counts as a reference.
        var references = doc.Descendants()
            .Where(e => e.Name.LocalName is "PackageReference" or "GlobalPackageReference")
            .Select(e => (Package: (string?)e.Attribute("Include") ?? (string?)e.Attribute("Update"), Element: e))
            .Where(x => x.Package is not null)
            .Select(x => new PackageUse(
                x.Package!,
                HasVersion: x.Element.Name.LocalName == "PackageReference" && (x.Element.Attribute("Version") is not null || x.Element.Element(x.Element.Name.Namespace + "Version") is not null),
                HasVersionOverride: x.Element.Attribute("VersionOverride") is not null || x.Element.Element(x.Element.Name.Namespace + "VersionOverride") is not null))
            .ToList();

        var versions = doc.Descendants()
            .Where(e => e.Name.LocalName == "PackageVersion")
            .Select(e => (string?)e.Attribute("Include") ?? (string?)e.Attribute("Update"))
            .OfType<string>()
            .ToList();

        bool PropertyIs(string name, string value) =>
            doc.Descendants().Any(e => e.Name.LocalName == name && string.Equals(e.Value.Trim(), value, StringComparison.OrdinalIgnoreCase));

        return new BuildFile(repoPath, references, versions,
            DisablesCentralVersions: PropertyIs("ManagePackageVersionsCentrally", "false"),
            DeclaresTestProject: PropertyIs("IsTestProject", "true"));
    }
}
