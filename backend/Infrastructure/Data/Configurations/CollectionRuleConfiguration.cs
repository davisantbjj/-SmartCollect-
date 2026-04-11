namespace SmartCollect.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartCollect.Domain.Entities;

public class CollectionRuleConfiguration : IEntityTypeConfiguration<CollectionRule>
{
    public void Configure(EntityTypeBuilder<CollectionRule> builder)
    {
        builder.ToTable("collection_rules");
        builder.HasIndex(r => r.TenantId);
        builder.HasIndex(r => new { r.TenantId, r.Active });
    }
}
