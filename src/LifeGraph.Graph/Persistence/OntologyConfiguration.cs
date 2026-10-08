using LifeGraph.Graph.Contracts;
using LifeGraph.Graph.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LifeGraph.Graph.Persistence;

internal sealed class PropertyDefinitionConfiguration : IEntityTypeConfiguration<PropertyDefinition>
{
    public void Configure(EntityTypeBuilder<PropertyDefinition> definition)
    {
        definition.ToTable("property_definitions", table => table.HasEnumCheck<PropertyValueKind>("value_kind"));
        definition.HasAccountKeys(entity => entity.Id, entity => new { entity.AccountId, entity.Id });
        definition.Property(entity => entity.Id).ValueGeneratedNever();
        definition.Property(entity => entity.Name).HasMaxLength(GraphLimits.NameMaxLength);
        definition.Property(entity => entity.ValueKind).StoredAsSnakeCase();
        definition.Property(entity => entity.Options).StoredAsJsonList();
        definition.HasRowVersion();
        definition.HasIndex(entity => new { entity.AccountId, entity.Name })
            .IsUnique()
            .HasDatabaseName(GraphConstraints.PropertyNameUnique);
    }
}

internal sealed class NodeTypeConfiguration : IEntityTypeConfiguration<NodeType>
{
    public void Configure(EntityTypeBuilder<NodeType> type)
    {
        type.ToTable("types");
        type.HasAccountKeys(entity => entity.Id, entity => new { entity.AccountId, entity.Id });
        type.Property(entity => entity.Id).ValueGeneratedNever();
        type.Property(entity => entity.Name).HasMaxLength(GraphLimits.NameMaxLength);
        type.HasRowVersion();
        type.HasIndex(entity => new { entity.AccountId, entity.Name })
            .IsUnique()
            .HasDatabaseName(GraphConstraints.TypeNameUnique);

        type.HasMany(entity => entity.Properties)
            .WithOne()
            .HasForeignKey(property => new { property.AccountId, property.TypeId })
            .HasPrincipalKey(entity => new { entity.AccountId, entity.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_type_properties_type");
        type.Navigation(entity => entity.Properties).HasField("_properties");
    }
}

internal sealed class TypePropertyConfiguration : IEntityTypeConfiguration<TypeProperty>
{
    public void Configure(EntityTypeBuilder<TypeProperty> property)
    {
        property.ToTable("type_properties", table =>
            table.HasCheckConstraint("ck_type_properties_position", "position >= 0"));
        property.HasKey(entity => new { entity.TypeId, entity.PropertyDefinitionId }).HasName("pk_type_properties");
        property.HasAccountForeignKey();
        property.HasOne<PropertyDefinition>()
            .WithMany()
            .HasForeignKey(entity => new { entity.AccountId, entity.PropertyDefinitionId })
            .HasPrincipalKey(definition => new { definition.AccountId, definition.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_type_properties_property_definition");
    }
}
