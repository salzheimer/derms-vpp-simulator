using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Rva.Modules.GridTopology.Domain.Assets;
using Rva.Modules.GridTopology.Domain.Catalog;
using Rva.Modules.GridTopology.Domain.GridState;
using Rva.Modules.GridTopology.Domain.Hierarchy;
using Rva.Modules.GridTopology.Persistence.Outbox;
using Rva.SharedKernel.Identifiers;
using Rva.SharedKernel.Units;

namespace Rva.Modules.GridTopology.Persistence;

/// <summary>
/// The GridTopology module's view of the <c>topology</c> schema in rva_core. Maps the main tables only, never the
/// <c>_history</c> tables, and never creates or migrates the schema: rva_core_init.sql is the source of truth (D6).
/// Changes are saved only through <see cref="UnitOfWork"/>, which runs them in a transaction with the change context
/// the versioning triggers record; saving any other way throws.
/// </summary>
internal sealed class GridTopologyDbContext(DbContextOptions<GridTopologyDbContext> options) : DbContext(options)
{
    public const string Schema = "topology";

    /// <summary>Set by <see cref="UnitOfWork"/> while it commits; SaveChanges refuses to run otherwise.</summary>
    internal bool SaveAllowed { get; set; }

    public DbSet<TechnologyType> TechnologyTypes => Set<TechnologyType>();
    public DbSet<InterconnectionType> InterconnectionTypes => Set<InterconnectionType>();
    public DbSet<CustomerSegment> CustomerSegments => Set<CustomerSegment>();
    public DbSet<GridNodeType> GridNodeTypes => Set<GridNodeType>();
    public DbSet<AssetLifecycleState> AssetLifecycleStates => Set<AssetLifecycleState>();
    public DbSet<ParticipationState> ParticipationStates => Set<ParticipationState>();
    public DbSet<GridStateType> GridStateTypes => Set<GridStateType>();
    public DbSet<ProtocolMapping> ProtocolMappings => Set<ProtocolMapping>();
    public DbSet<HardwareProfile> HardwareProfiles => Set<HardwareProfile>();
    public DbSet<GridNodeRegistration> GridNodes => Set<GridNodeRegistration>();
    public DbSet<Substation> Substations => Set<Substation>();
    public DbSet<Feeder> Feeders => Set<Feeder>();
    public DbSet<NeighborhoodTransformer> NeighborhoodTransformers => Set<NeighborhoodTransformer>();
    public DbSet<Premise> Premises => Set<Premise>();
    public DbSet<Microgrid> Microgrids => Set<Microgrid>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetComponent> AssetComponents => Set<AssetComponent>();
    public DbSet<GridStateEvent> GridStateEvents => Set<GridStateEvent>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    /// <summary>Provider settings shared by the module registration and the tests.</summary>
    internal static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            // EF logs its own failures at Error. An unexpected failure is logged once, at Error, where it is handled
            // (the host's exception handler); an expected one is not an error. EF's entries stay, as Warnings.
            .ConfigureWarnings(warnings => warnings.Log(
                (RelationalEventId.CommandError, LogLevel.Warning),
                (RelationalEventId.ConnectionError, LogLevel.Warning),
                (RelationalEventId.TransactionError, LogLevel.Warning),
                (CoreEventId.SaveChangesFailed, LogLevel.Warning),
                (CoreEventId.QueryIterationFailed, LogLevel.Warning)));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GridTopologyDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Applies to nullable properties of these types too.
        configurationBuilder.Properties<AssetId>().HaveConversion<ValueConverters.AssetIdConverter>();
        configurationBuilder.Properties<GridNodeId>().HaveConversion<ValueConverters.GridNodeIdConverter>();
        configurationBuilder.Properties<SubstationId>().HaveConversion<ValueConverters.SubstationIdConverter>();
        configurationBuilder.Properties<FeederId>().HaveConversion<ValueConverters.FeederIdConverter>();
        configurationBuilder.Properties<TransformerId>().HaveConversion<ValueConverters.TransformerIdConverter>();
        configurationBuilder.Properties<PremiseId>().HaveConversion<ValueConverters.PremiseIdConverter>();
        configurationBuilder.Properties<MicrogridId>().HaveConversion<ValueConverters.MicrogridIdConverter>();
        configurationBuilder.Properties<Kilowatts>().HaveConversion<ValueConverters.KilowattsConverter>();
        configurationBuilder.Properties<KilowattHours>().HaveConversion<ValueConverters.KilowattHoursConverter>();
        configurationBuilder.Properties<KilovoltAmperes>().HaveConversion<ValueConverters.KilovoltAmperesConverter>();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureSaveAllowed();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureSaveAllowed();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnsureSaveAllowed()
    {
        if (!SaveAllowed)
            throw new InvalidOperationException(
                "GridTopology changes are saved only through UnitOfWork, which sets the change context (actor, reason) the history tables record.");
    }
}
