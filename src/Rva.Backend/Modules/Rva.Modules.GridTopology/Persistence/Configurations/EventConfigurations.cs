using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Rva.Modules.GridTopology.Domain.GridState;
using Rva.Modules.GridTopology.Persistence.Outbox;

namespace Rva.Modules.GridTopology.Persistence.Configurations;

internal sealed class GridStateEventConfiguration : IEntityTypeConfiguration<GridStateEvent>
{
    public void Configure(EntityTypeBuilder<GridStateEvent> entity)
    {
        entity.ToTable("grid_state_events");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("grid_state_event_id").ValueGeneratedOnAdd();
        entity.Property(e => e.GridNodeId).HasColumnName("grid_node_id");
        entity.Property(e => e.AssetId).HasColumnName("asset_id");
        entity.Property(e => e.GridStateId).HasColumnName("grid_state_id");
        entity.Property(e => e.OccurredAt).HasColumnName("occurred_at");
        entity.Property(e => e.Reason).HasColumnName("reason").HasMaxLength(500);
        entity.Property(e => e.Details).HasColumnName("details").HasColumnType("jsonb");
        // Stamped by topology.record_grid_state_event from the clock and the transaction's change context.
        entity.Property(e => e.RecordedAt).HasColumnName("recorded_at").SetByDatabase();
        entity.Property(e => e.ActorId).HasColumnName("actor_id").SetByDatabase();
        entity.Property(e => e.ActorType).HasColumnName("actor_type").HasMaxLength(20).SetByDatabase();
    }
}

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> entity)
    {
        entity.ToTable("outbox_messages");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).HasColumnName("outbox_message_id").ValueGeneratedOnAdd();
        entity.Property(e => e.EventId).HasColumnName("event_id").ValueGeneratedNever();
        entity.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(200);
        entity.Property(e => e.Payload).HasColumnName("payload").HasColumnType("jsonb");
        entity.Property(e => e.OccurredAt).HasColumnName("occurred_at").SetByDatabase();
        entity.Property(e => e.PublishedAt).HasColumnName("published_at");
        entity.Property(e => e.AttemptCount).HasColumnName("attempt_count");
    }
}
