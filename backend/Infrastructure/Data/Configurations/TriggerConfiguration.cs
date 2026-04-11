namespace SmartCollect.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartCollect.Domain.Entities;

public class TriggerConfiguration : IEntityTypeConfiguration<Trigger>
{
    public void Configure(EntityTypeBuilder<Trigger> builder)
    {
        builder.ToTable("triggers");
        builder.Property(t => t.Channel).HasConversion<string>();
        builder.Property(t => t.Reference).HasConversion<string>();
    }
}
