using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NpgsqlTypes;

namespace Rva.Modules.GridTopology.Persistence.Configurations;

internal static class MappingExtensions
{
    public const string CreatedAt = "CreatedAt";
    public const string SysPeriod = "SysPeriod";
    public const string RowVersion = "RowVersion";

    /// <summary>
    /// A column the database always sets (by default or trigger): never sent in an INSERT or UPDATE, read back after
    /// each save. Covers created_at, sys_period, grid_node_id, recorded_at.
    /// </summary>
    public static PropertyBuilder<T> SetByDatabase<T>(this PropertyBuilder<T> property)
    {
        property.ValueGeneratedOnAddOrUpdate();
        property.Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        property.Metadata.SetAfterSaveBehavior(PropertySaveBehavior.Ignore);
        return property;
    }

    /// <summary>
    /// The system-versioning columns every versioned topology table has (topology.enable_versioning), as shadow
    /// properties set only by topology.version_row. Reads use lower(sys_period) as the version's ValidFrom.
    /// </summary>
    public static EntityTypeBuilder<T> IsSystemVersioned<T>(this EntityTypeBuilder<T> entity) where T : class
    {
        entity.Property<DateTimeOffset>(CreatedAt).HasColumnName("created_at").SetByDatabase();
        entity.Property<NpgsqlRange<DateTime>>(SysPeriod).HasColumnName("sys_period").HasColumnType("tstzrange").SetByDatabase();
        return entity;
    }

    /// <summary>Optimistic concurrency on PostgreSQL's xmin system column: a lost update fails instead of overwriting.</summary>
    public static EntityTypeBuilder<T> HasRowVersion<T>(this EntityTypeBuilder<T> entity) where T : class
    {
        entity.Property<uint>(RowVersion).HasColumnName("xmin").HasColumnType("xid").IsRowVersion();
        return entity;
    }
}
