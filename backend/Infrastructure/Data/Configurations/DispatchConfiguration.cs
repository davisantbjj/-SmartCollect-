namespace SmartCollect.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartCollect.Domain.Entities;

public class DispatchConfiguration : IEntityTypeConfiguration<Dispatch>
{
    public void Configure(EntityTypeBuilder<Dispatch> builder)
    {
        builder.ToTable("dispatches");
        builder.HasIndex(d => d.TitleId);
        builder.HasIndex(d => new { d.Status, d.ScheduledFor });
        builder.Property(d => d.Channel).HasConversion<string>();
        builder.Property(d => d.Status).HasConversion<string>();
    }
}
