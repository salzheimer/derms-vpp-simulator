using System.Reflection;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using Rva.ArchitectureTests.Support;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Rva.ArchitectureTests;

/// <summary>
/// Type-level boundaries on the compiled assemblies (.ai/rules/csharp-monolith.md sections 3-5).
/// Rules that find no types yet (e.g. an empty shared kernel) pass until the first type appears;
/// ArchUnitNET is used where it sees both sides of a dependency (backend types), reflection elsewhere.
/// </summary>
public class TypeRuleTests
{
    // The base class library = the assemblies of the Microsoft.NETCore.App shared framework this test runs on.
    // ASP.NET Core, EF Core, and NuGet packages are not in it.
    private static readonly HashSet<string> BaseClassLibrary =
        Directory.EnumerateFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    // Uses compiled assembly references, which list only assemblies the code actually uses.
    private static IEnumerable<string> ReferencesOutside(Assembly assembly, params string[] alsoAllowed) =>
        assembly.GetReferencedAssemblies()
            .Select(n => n.Name!)
            .Where(n => !BaseClassLibrary.Contains(n) && !alsoAllowed.Contains(n, StringComparer.Ordinal));

    [Fact]
    public void Shared_kernel_depends_only_on_the_base_class_library()
    {
        var kernel = BackendAssemblies.SharedKernel;
        var outside = ReferencesOutside(kernel).ToList();

        Assert.True(outside.Count == 0,
            $"{kernel.GetName().Name} uses assemblies outside the .NET base class library: {string.Join(", ", outside)}");
    }

    [Fact]
    public void Contracts_depend_only_on_the_base_class_library_and_shared_kernel()
    {
        var kernelName = BackendAssemblies.SharedKernel.GetName().Name!;
        var problems = BackendAssemblies.Contracts
            .SelectMany(c => ReferencesOutside(c, kernelName).Select(r => $"{c.GetName().Name} uses {r}"))
            .ToList();

        Assert.True(problems.Count == 0,
            ".Contracts projects may use only the .NET base class library and Rva.SharedKernel:\n" + string.Join("\n", problems));
    }

    [Fact]
    public void Only_the_module_registration_class_is_public_in_a_module()
    {
        var problems = new List<string>();
        foreach (var module in BackendAssemblies.Modules)
        {
            var area = module.GetName().Name![ModuleConventions.Prefix.Length..];
            var allowed = $"{module.GetName().Name}.{area}Module";

            problems.AddRange(module.GetExportedTypes()
                .Where(t => t.FullName != allowed)
                .Select(t => $"{t.FullName} is public; module types must be internal (only {allowed} is public)"));

            if (module.GetType(allowed) is null)
                problems.Add($"{module.GetName().Name} is missing its registration class {allowed}");
        }

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public void Nothing_outside_a_module_uses_its_DbContext()
    {
        // Project references already stop modules from seeing each other; the host is the remaining risk.
        foreach (var module in BackendAssemblies.Modules)
        {
            IArchRule rule = Types().That().ResideInAssembly(BackendAssemblies.Host)
                .Should().NotDependOnAny(Types().That().ResideInAssembly(module).And().HaveNameEndingWith("DbContext"))
                .Because("each module owns its data; other code reaches it only through the module's contracts")
                .WithoutRequiringPositiveResults();

            rule.Check(BackendAssemblies.Architecture);
        }
    }

    [Fact]
    public void Domain_types_carry_no_ORM_attributes()
    {
        // Domain types: everything in the shared kernel and contracts, and module types in a .Domain namespace.
        var domainTypes = BackendAssemblies.Contracts.Append(BackendAssemblies.SharedKernel)
            .SelectMany(a => a.GetTypes())
            .Concat(BackendAssemblies.Modules.SelectMany(a => a.GetTypes()).Where(t => t.Namespace?.Split('.').Contains("Domain") == true));

        var problems = domainTypes
            .SelectMany(t => new MemberInfo[] { t }.Concat(t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)))
            .SelectMany(m => m.GetCustomAttributesData().Select(a => (Member: m, Attribute: a.AttributeType)))
            .Where(x => IsOrmAttribute(x.Attribute))
            .Select(x => $"{(x.Member as Type ?? x.Member.DeclaringType)?.FullName}.{x.Member.Name}: [{x.Attribute.Name}]; map with Fluent API in the module's Persistence/ folder")
            .ToList();

        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    private static bool IsOrmAttribute(Type attribute) =>
        attribute.Namespace == "System.ComponentModel.DataAnnotations.Schema"
        || attribute.Namespace?.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) == true
        || attribute.FullName == "System.ComponentModel.DataAnnotations.KeyAttribute";
}
