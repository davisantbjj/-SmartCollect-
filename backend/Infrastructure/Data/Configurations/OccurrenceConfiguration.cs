namespace SmartCollect.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartCollect.Domain.Entities;

public class OccurrenceConfiguration : IEntityTypeConfiguration<Occurrence>
{
    public void Configure(EntityTypeBuilder<Occurrence> builder)
    {
        builder.ToTable("occurrences");
        builder.HasIndex(o => o.TitleId);
        builder.HasIndex(o => o.OccurrenceDate);
        builder.Property(o => o.UpdatedStatus).HasConversion<string>();
    }
}
