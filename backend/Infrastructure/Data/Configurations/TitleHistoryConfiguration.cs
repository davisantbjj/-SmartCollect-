namespace SmartCollect.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartCollect.Domain.Entities;

public class TitleHistoryConfiguration : IEntityTypeConfiguration<TitleHistory>
{
    public void Configure(EntityTypeBuilder<TitleHistory> builder)
    {
        builder.ToTable("title_histories");
        builder.HasIndex(h => h.TitleId);
        builder.HasIndex(h => new { h.TenantId, h.CreatedAt });
        builder.Property(h => h.Action).HasMaxLength(80);
        builder.Property(h => h.Description).HasMaxLength(600);

        builder.HasOne(h => h.Title)
            .WithMany(t => t.Histories)
            .HasForeignKey(h => h.TitleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
