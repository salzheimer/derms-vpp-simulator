using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.Fluent;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Rva.ArchitectureTests;

public class ModularMonolithArchitectureTests
{
    // Load the compiled backend assemblies once for performance
    private static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(
            typeof(Rva.Domain.Asset).Assembly,
            typeof(Rva.Modules.GridTopology.IGridTopologyService).Assembly,
            typeof(Rva.Modules.Telemetry.TelemetryChannelWorker).Assembly,
            typeof(Rva.Modules.Simulation.SimulationEngine).Assembly
        )
        .Build();

    // Define layers / namespaces for the rules
    private readonly IObjectProvider<IType> DomainLayer =
        Types().That().ResideInNamespace("Rva.Domain").As("Rva.Domain Kernel");

    private readonly IObjectProvider<IType> GridTopologyModule =
        Types().That().ResideInNamespace("Rva.Modules.GridTopology").As("GridTopology Module");

    private readonly IObjectProvider<IType> TelemetryModule =
        Types().That().ResideInNamespace("Rva.Modules.Telemetry").As("Telemetry Module");

    [Fact]
    public void Modules_ShouldNot_DependOnEachOther()
    {
        // Enforce that individual business modules cannot directly reference one another
        IArchRule rule = Types().That().ResideInNamespace("Rva.Modules.GridTopology")
            .Should().NotDependOnAny(Types().That().ResideInNamespace("Rva.Modules.Telemetry"))
            .Because("modules must remain strictly decoupled; use integration events instead.");

        rule.Check(Architecture);
    }

    [Fact]
    public void DomainKernel_ShouldHave_ZeroOutgoingDependencies()
    {
        // Enforce that Rva.Domain never leaks out to touch modules or infrastructure
        IArchRule rule = DomainLayer
            .Should().NotDependOnAny(Types().That().ResideInNamespace("Rva.Modules"))
            .Because("the shared kernel must remain pure and independent of operational modules.");

        rule.Check(Architecture);
    }

    [Fact]
    public void DomainModels_ShouldNot_HaveEntityFrameworkAttributes()
    {
        // Enforce that domain objects in Rva.Domain are pure POCOs without data annotations like [Table]
        IArchRule rule = Classes().That().Are(DomainLayer)
            .Should().NotHaveAnyAttributes(Types().That().HaveNameMatching("TableAttribute"))
            .Because("persistence details and ORM attributes must stay out of Rva.Domain.");

        rule.Check(Architecture);
    }
}