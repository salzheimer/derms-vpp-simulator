using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rva.Modules.GridTopology.Domain.Hierarchy;

namespace Rva.Modules.GridTopology.Persistence.Configurations;

/// <summary>Columns every node table shares. grid_node_id is assigned by trigger (topology.assign_grid_node).</summary>
internal abstract class GridNodeConfiguration<T>(string table, string codeColumn) : IEntityTypeConfiguration<T> where T : GridNode
{
    public const string GeographyPoint = "geography (point, 4326)";

    public virtual void Configure(EntityTypeBuilder<T> entity)
    {
        entity.ToTable(table);
        entity.Property(e => e.GridNodeId).HasColumnName("grid_node_id").SetByDatabase();
        entity.Property(e => e.Code).HasColumnName(codeColumn).HasMaxLength(50);
        entity.IsSystemVersioned();
    }
}

internal sealed class GridNodeRegistrationConfiguration : IEntityTypeConfiguration<GridNodeRegistration>
{
    public void Configure(EntityTypeBuilder<GridNodeRegistration> entity)
    {
        entity.ToTable("grid_nodes");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("grid_node_id").ValueGeneratedOnAdd();
        entity.Property(e => e.GridNodeTypeId).HasColumnName("grid_node_type_id");
        entity.Property(e => e.CreatedAt).HasColumnName("created_at").SetByDatabase();
    }
}

internal sealed class SubstationConfiguration() : GridNodeConfiguration<Substation>("substations", "substation_code")
{
    public override void Configure(EntityTypeBuilder<Substation> entity)
    {
        base.Configure(entity);
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("substation_id").ValueGeneratedOnAdd();
        entity.Property(e => e.RatedCapacityKva).HasColumnName("rated_capacity_kva");
        entity.Property(e => e.Location).HasColumnName("location").HasColumnType(GeographyPoint);
    }
}

internal sealed class FeederConfiguration() : GridNodeConfiguration<Feeder>("feeders", "feeder_code")
{
    public override void Configure(EntityTypeBuilder<Feeder> entity)
    {
        base.Configure(entity);
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("feeder_id").ValueGeneratedOnAdd();
        entity.Property(e => e.SubstationId).HasColumnName("substation_id");
        entity.Property(e => e.NominalVoltageKv).HasColumnName("nominal_voltage_kv");
        entity.Property(e => e.RatedCapacityKva).HasColumnName("rated_capacity_kva");
        entity.Property(e => e.LineGeometry).HasColumnName("line_geometry").HasColumnType("geometry (linestring, 4326)");
    }
}

internal sealed class NeighborhoodTransformerConfiguration() : GridNodeConfiguration<NeighborhoodTransformer>("neighborhood_transformers", "neighborhood_transformer_code")
{
    public override void Configure(EntityTypeBuilder<NeighborhoodTransformer> entity)
    {
        base.Configure(entity);
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("neighborhood_transformer_id").ValueGeneratedOnAdd();
        entity.Property(e => e.FeederId).HasColumnName("feeder_id");
        entity.Property(e => e.RatedCapacityKva).HasColumnName("rated_capacity_kva");
        entity.Property(e => e.SecondaryVoltageV).HasColumnName("secondary_voltage_v");
        entity.Property(e => e.Location).HasColumnName("location").HasColumnType(GeographyPoint);
    }
}

internal sealed class PremiseConfiguration() : GridNodeConfiguration<Premise>("premises", "premise_code")
{
    public override void Configure(EntityTypeBuilder<Premise> entity)
    {
        base.Configure(entity);
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("premise_id").ValueGeneratedOnAdd();
        entity.Property(e => e.TransformerId).HasColumnName("neighborhood_transformer_id");
        entity.Property(e => e.FeederId).HasColumnName("feeder_id");
        entity.Property(e => e.CustomerSegmentId).HasColumnName("customer_segment_id");
        entity.Property(e => e.ServiceCapacityKva).HasColumnName("service_capacity_kva");
        entity.Property(e => e.ExportLimitKw).HasColumnName("export_limit_kw");
        entity.Property(e => e.CanIsland).HasColumnName("can_island");
        entity.Property(e => e.Address).HasColumnName("address").HasMaxLength(255);
        entity.Property(e => e.Location).HasColumnName("location").HasColumnType(GeographyPoint);
    }
}

internal sealed class MicrogridConfiguration() : GridNodeConfiguration<Microgrid>("microgrids", "microgrid_code")
{
    public override void Configure(EntityTypeBuilder<Microgrid> entity)
    {
        base.Configure(entity);
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("microgrid_id").ValueGeneratedOnAdd();
        entity.Property(e => e.FeederId).HasColumnName("feeder_id");
        entity.Property(e => e.SubstationId).HasColumnName("substation_id");
        entity.Property(e => e.CanIsland).HasColumnName("can_island");
        entity.Property(e => e.ImportLimitKw).HasColumnName("import_limit_kw");
        entity.Property(e => e.ExportLimitKw).HasColumnName("export_limit_kw");
    }
}
