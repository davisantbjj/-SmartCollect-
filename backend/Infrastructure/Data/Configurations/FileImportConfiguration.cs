namespace SmartCollect.Infrastructure.Data.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartCollect.Domain.Entities;

public class FileImportConfiguration : IEntityTypeConfiguration<FileImport>
{
    public void Configure(EntityTypeBuilder<FileImport> builder)
    {
        builder.ToTable("file_imports");
        builder.HasIndex(f => f.TenantId);
        builder.HasIndex(f => new { f.TenantId, f.Status });
        builder.Property(f => f.Type).HasConversion<string>();
        builder.Property(f => f.Status).HasConversion<string>();
    }
}
