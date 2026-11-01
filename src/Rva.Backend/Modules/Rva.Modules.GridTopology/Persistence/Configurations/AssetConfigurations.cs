using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rva.Modules.GridTopology.Domain.Assets;

namespace Rva.Modules.GridTopology.Persistence.Configurations;

internal sealed class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> entity)
    {
        entity.ToTable("assets");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("asset_id").ValueGeneratedOnAdd();
        entity.Property(e => e.Code).HasColumnName("asset_code").HasMaxLength(50);
        entity.Property(e => e.PremiseId).HasColumnName("premise_id");
        entity.Property(e => e.TransformerId).HasColumnName("neighborhood_transformer_id");
        entity.Property(e => e.FeederId).HasColumnName("feeder_id");
        entity.Property(e => e.SubstationId).HasColumnName("substation_id");
        entity.Property(e => e.MicrogridId).HasColumnName("microgrid_id");
        entity.Property(e => e.InterconnectionTypeId).HasColumnName("interconnection_type_id");
        entity.Property(e => e.ControlHardwareProfileId).HasColumnName("control_hardware_profile_id");
        entity.Property(e => e.LifecycleStateId).HasColumnName("asset_lifecycle_state_id");
        entity.Property(e => e.ParticipationStateId).HasColumnName("participation_state_id");
        entity.Property(e => e.InServiceDate).HasColumnName("in_service_date");
        entity.Property(e => e.DecommissionedDate).HasColumnName("decommissioned_date");
        entity.Property(e => e.RatedPMin).HasColumnName("rated_p_min_kw");
        entity.Property(e => e.RatedPMax).HasColumnName("rated_p_max_kw");
        entity.Property(e => e.RatedKva).HasColumnName("rated_kva");
        entity.Property(e => e.ExportLimit).HasColumnName("export_limit_kw");
        entity.IsSystemVersioned().HasRowVersion();

        entity.HasMany(e => e.Components).WithOne().HasForeignKey(c => c.AssetId);
        entity.Navigation(e => e.Components).UsePropertyAccessMode(PropertyAccessMode.Field).HasField("components");
    }
}

internal sealed class AssetComponentConfiguration : IEntityTypeConfiguration<AssetComponent>
{
    public void Configure(EntityTypeBuilder<AssetComponent> entity)
    {
        entity.ToTable("asset_components");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("asset_component_id").ValueGeneratedOnAdd();
        entity.Property(e => e.AssetId).HasColumnName("asset_id");
        entity.Property(e => e.TechnologyTypeId).HasColumnName("technology_type_id");
        entity.Property(e => e.HardwareProfileId).HasColumnName("hardware_profile_id");
        entity.Property(e => e.UnitCount).HasColumnName("unit_count");
        entity.Property(e => e.RatedKw).HasColumnName("rated_kw");
        entity.Property(e => e.RatedKwh).HasColumnName("rated_kwh");
        entity.IsSystemVersioned().HasRowVersion();
    }
}
