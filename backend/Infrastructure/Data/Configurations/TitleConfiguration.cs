namespace SmartCollect.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartCollect.Domain.Entities;

public class TitleConfiguration : IEntityTypeConfiguration<Title>
{
    public void Configure(EntityTypeBuilder<Title> builder)
    {
        builder.ToTable("titles");
        builder.HasIndex(t => new { t.TenantId, t.UniqueCode }).IsUnique();
        builder.Property(t => t.Amount).HasPrecision(18, 2);
        builder.Property(t => t.Status).HasConversion<string>();
    }
}
