using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rva.Modules.GridTopology.Domain.Catalog;

namespace Rva.Modules.GridTopology.Persistence.Configurations;

internal abstract class LookupConfiguration<T>(string table, string idColumn) : IEntityTypeConfiguration<T> where T : LookupEntry
{
    public virtual void Configure(EntityTypeBuilder<T> entity)
    {
        entity.ToTable(table);
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName(idColumn).ValueGeneratedOnAdd();
        entity.Property(e => e.Code).HasColumnName("internal_code").HasMaxLength(50);
        entity.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(100);
        entity.IsSystemVersioned();
    }
}

internal sealed class TechnologyTypeConfiguration() : LookupConfiguration<TechnologyType>("technology_types", "technology_type_id");
internal sealed class InterconnectionTypeConfiguration() : LookupConfiguration<InterconnectionType>("interconnection_types", "interconnection_type_id");
internal sealed class CustomerSegmentConfiguration() : LookupConfiguration<CustomerSegment>("customer_segments", "customer_segment_id");
internal sealed class GridNodeTypeConfiguration() : LookupConfiguration<GridNodeType>("grid_node_types", "grid_node_type_id");
internal sealed class AssetLifecycleStateConfiguration() : LookupConfiguration<AssetLifecycleState>("asset_lifecycle_states", "asset_lifecycle_state_id");
internal sealed class ParticipationStateConfiguration() : LookupConfiguration<ParticipationState>("participation_states", "participation_state_id");

internal sealed class GridStateTypeConfiguration() : LookupConfiguration<GridStateType>("grid_states", "grid_state_id")
{
    public override void Configure(EntityTypeBuilder<GridStateType> entity)
    {
        base.Configure(entity);
        entity.Property(e => e.IsGridConnected).HasColumnName("is_grid_connected");
        entity.Property(e => e.IsEnergized).HasColumnName("is_energized");
    }
}

internal sealed class ProtocolMappingConfiguration : IEntityTypeConfiguration<ProtocolMapping>
{
    public void Configure(EntityTypeBuilder<ProtocolMapping> entity)
    {
        entity.ToTable("protocol_mappings");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("protocol_mapping_id").ValueGeneratedOnAdd();
        entity.Property(e => e.TechnologyTypeId).HasColumnName("technology_type_id");
        entity.Property(e => e.ProtocolStandard).HasColumnName("protocol_standard").HasMaxLength(50);
        entity.Property(e => e.ResourceCategory).HasColumnName("resource_category").HasMaxLength(100);
        entity.Property(e => e.AssetTypeCode).HasColumnName("asset_type_code").HasMaxLength(100);
        entity.Property(e => e.ExtraAttributes).HasColumnName("extra_attributes").HasColumnType("jsonb");
        entity.IsSystemVersioned();
    }
}

internal sealed class HardwareProfileConfiguration : IEntityTypeConfiguration<HardwareProfile>
{
    public void Configure(EntityTypeBuilder<HardwareProfile> entity)
    {
        entity.ToTable("hardware_profiles");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("hardware_profile_id").ValueGeneratedOnAdd();
        entity.Property(e => e.Code).HasColumnName("hardware_profile_code").HasMaxLength(50);
        entity.Property(e => e.Manufacturer).HasColumnName("manufacturer").HasMaxLength(100);
        entity.Property(e => e.Model).HasColumnName("model").HasMaxLength(100);
        entity.Property(e => e.TechnologyTypeId).HasColumnName("technology_type_id");
        entity.Property(e => e.RatedKw).HasColumnName("rated_kw");
        entity.Property(e => e.RatedKva).HasColumnName("rated_kva");
        entity.Property(e => e.RatedKwh).HasColumnName("rated_kwh");
        entity.Property(e => e.RoundTripEfficiency).HasColumnName("round_trip_efficiency");
        entity.IsSystemVersioned();
    }
}
