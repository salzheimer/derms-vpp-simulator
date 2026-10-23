using System.Xml.Linq;
using Rva.ArchitectureTests.Support;

namespace Rva.ArchitectureTests;

/// <summary>
/// Proves the test framework rules reject each forbidden shape, so a passing TestFrameworkTests run means
/// "no violations" rather than "the rule checks nothing". Uses synthetic build files.
/// </summary>
public class TestFrameworkRulesTests
{
    private static BuildFile Central(params string[] versions) => new("Directory.Packages.props", [], versions);

    private static BuildFile TestProject(string path, params string[] packages) =>
        new(path, packages.Select(p => new PackageUse(p)).ToList(), []);

    private static List<BuildFile> ValidBaseline() =>
    [
        Central("xunit.v3", "TngTech.ArchUnitNET.xUnitV3", "Microsoft.Testing.Extensions.CodeCoverage", "Serilog.AspNetCore"),
        TestProject("tests/Rva.ArchitectureTests/Rva.ArchitectureTests.csproj", "xunit.v3", "TngTech.ArchUnitNET.xUnitV3", "Microsoft.Testing.Extensions.CodeCoverage"),
        TestProject("src/Rva.Backend/Rva.Host/Rva.Host.csproj", "Serilog.AspNetCore"),
        new("src/Rva.Backend/Directory.Build.props", [], []),
    ];

    private static IReadOnlyList<string> ViolationsWith(params BuildFile[] extra) =>
        TestFrameworkRules.Violations(ValidBaseline().Concat(extra).ToList());

    private static IReadOnlyList<string> ViolationsReplacing(string path, BuildFile replacement)
    {
        var files = ValidBaseline();
        files[files.FindIndex(f => f.RepoPath == path)] = replacement;
        return TestFrameworkRules.Violations(files);
    }

    [Fact]
    public void Valid_build_has_no_violations() =>
        Assert.Empty(TestFrameworkRules.Violations(ValidBaseline()));

    [Theory]
    [InlineData("xunit", "use xunit.v3")]
    [InlineData("xunit.runner.visualstudio", "remove it")]
    [InlineData("Microsoft.NET.Test.Sdk", "remove it")]
    [InlineData("NUnit", "use xunit.v3")]
    [InlineData("NUnit3TestAdapter", "use xunit.v3")]
    [InlineData("MSTest.TestFramework", "use xunit.v3")]
    [InlineData("TUnit", "use xunit.v3")]
    [InlineData("coverlet.collector", "use Microsoft.Testing.Extensions.CodeCoverage")]
    [InlineData("TngTech.ArchUnitNET.xUnit", "use TngTech.ArchUnitNET.xUnitV3")]
    public void Forbidden_package_in_central_versions_is_rejected(string package, string fix)
    {
        var violations = ViolationsReplacing("Directory.Packages.props", Central("xunit.v3", package));

        Assert.Contains(violations, v => v.StartsWith($"Directory.Packages.props: {package} is not an approved test package; {fix}"));
    }

    [Fact]
    public void Forbidden_package_referenced_by_a_project_is_rejected() =>
        Assert.Contains(
            ViolationsReplacing("tests/Rva.ArchitectureTests/Rva.ArchitectureTests.csproj",
                TestProject("tests/Rva.ArchitectureTests/Rva.ArchitectureTests.csproj", "xunit.v3", "Microsoft.NET.Test.Sdk")),
            v => v.StartsWith("tests/Rva.ArchitectureTests/Rva.ArchitectureTests.csproj: Microsoft.NET.Test.Sdk is not an approved test package"));

    [Fact]
    public void Approved_xunit_v3_packages_are_not_mistaken_for_v2() =>
        Assert.Empty(ViolationsReplacing("Directory.Packages.props", Central("xunit.v3", "xunit.v3.mtp-v2", "xunit.v3.assert", "TngTech.ArchUnitNET.xUnitV3")));

    [Theory]
    [InlineData("Moq")]
    [InlineData("FluentAssertions")]
    [InlineData("Testcontainers.PostgreSql")]
    public void Unapproved_package_in_a_test_project_is_rejected(string package) =>
        Assert.Contains(
            ViolationsReplacing("tests/Rva.ArchitectureTests/Rva.ArchitectureTests.csproj",
                TestProject("tests/Rva.ArchitectureTests/Rva.ArchitectureTests.csproj", "xunit.v3", package)),
            v => v.StartsWith($"tests/Rva.ArchitectureTests/Rva.ArchitectureTests.csproj: {package} is not on the approved test package list"));

    [Fact]
    public void Approved_list_does_not_apply_to_non_test_projects() =>
        Assert.Empty(ViolationsReplacing("src/Rva.Backend/Rva.Host/Rva.Host.csproj",
            TestProject("src/Rva.Backend/Rva.Host/Rva.Host.csproj", "Serilog.AspNetCore", "Microsoft.EntityFrameworkCore")));

    [Fact]
    public void Test_project_without_xunit_v3_is_rejected() =>
        Assert.Contains(
            ViolationsWith(TestProject("tests/Rva.Other.Tests/Rva.Other.Tests.csproj", "Microsoft.AspNetCore.Mvc.Testing")),
            v => v.StartsWith("tests/Rva.Other.Tests/Rva.Other.Tests.csproj: test project does not reference xunit.v3"));

    [Fact]
    public void Project_marked_IsTestProject_without_xunit_v3_is_rejected() =>
        Assert.Contains(
            ViolationsWith(new BuildFile("tests/Rva.Other.Tests/Rva.Other.Tests.csproj", [], [], DeclaresTestProject: true)),
            v => v.StartsWith("tests/Rva.Other.Tests/Rva.Other.Tests.csproj: test project does not reference xunit.v3"));

    [Fact]
    public void Test_project_outside_tests_folder_is_rejected() =>
        Assert.Contains(
            ViolationsWith(TestProject("src/Rva.Simulator.Tests/Rva.Simulator.Tests.csproj", "xunit.v3")),
            v => v.StartsWith("src/Rva.Simulator.Tests/Rva.Simulator.Tests.csproj: test projects live under tests/"));

    [Fact]
    public void Nested_central_packages_file_is_rejected() =>
        Assert.Contains(
            ViolationsWith(new BuildFile("tests/Directory.Packages.props", [], ["xunit.v3"])),
            v => v.StartsWith("expected exactly one Directory.Packages.props, at the repo root; found: Directory.Packages.props, tests/Directory.Packages.props"));

    [Fact]
    public void Missing_central_packages_file_is_rejected()
    {
        var files = ValidBaseline().Where(f => f.RepoPath != "Directory.Packages.props").ToList();

        Assert.Contains(TestFrameworkRules.Violations(files), v => v.StartsWith("expected exactly one Directory.Packages.props, at the repo root; found: none"));
    }

    [Fact]
    public void Disabling_central_package_management_is_rejected() =>
        Assert.Contains(
            ViolationsReplacing("src/Rva.Backend/Directory.Build.props", new BuildFile("src/Rva.Backend/Directory.Build.props", [], [], DisablesCentralVersions: true)),
            v => v.StartsWith("src/Rva.Backend/Directory.Build.props: sets ManagePackageVersionsCentrally to false"));

    [Fact]
    public void Package_versions_outside_the_central_file_are_rejected() =>
        Assert.Contains(
            ViolationsReplacing("src/Rva.Backend/Directory.Build.props", new BuildFile("src/Rva.Backend/Directory.Build.props", [], ["NUnit"])),
            v => v.StartsWith("src/Rva.Backend/Directory.Build.props: declares PackageVersion items"));

    [Theory]
    [InlineData(true, false, "Version")]
    [InlineData(false, true, "VersionOverride")]
    public void Inline_version_on_a_package_reference_is_rejected(bool version, bool versionOverride, string attribute) =>
        Assert.Contains(
            ViolationsReplacing("src/Rva.Backend/Rva.Host/Rva.Host.csproj",
                new BuildFile("src/Rva.Backend/Rva.Host/Rva.Host.csproj", [new PackageUse("Serilog.AspNetCore", version, versionOverride)], [])),
            v => v.StartsWith($"src/Rva.Backend/Rva.Host/Rva.Host.csproj: PackageReference Serilog.AspNetCore sets {attribute}"));

    [Fact]
    public void Parser_reads_versions_overrides_global_references_and_properties()
    {
        var file = BuildFiles.Parse("tests/X.Tests/X.Tests.csproj", XDocument.Parse("""
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally> False </ManagePackageVersionsCentrally>
                <IsTestProject>true</IsTestProject>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="A" Version="1.0" />
                <PackageReference Include="B" VersionOverride="2.0" />
                <PackageReference Include="C"><Version>3.0</Version></PackageReference>
                <GlobalPackageReference Include="NUnit" Version="4.0" />
                <PackageVersion Include="D" Version="4.0" />
              </ItemGroup>
            </Project>
            """));

        Assert.Equal(
            [new PackageUse("A", true, false), new PackageUse("B", false, true), new PackageUse("C", true, false), new PackageUse("NUnit", false, false)],
            file.PackageReferences);
        Assert.Equal(["D"], file.PackageVersions);
        Assert.True(file.DisablesCentralVersions);
        Assert.True(file.DeclaresTestProject);
    }
}
