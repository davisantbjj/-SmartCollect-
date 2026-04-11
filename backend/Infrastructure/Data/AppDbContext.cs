namespace SmartCollect.Infrastructure.Data;

using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.Interfaces;
using SmartCollect.Domain.Common;
using SmartCollect.Domain.Entities;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Client> Clients { get; set; }
    public DbSet<Contact> Contacts { get; set; }
    public DbSet<Title> Titles { get; set; }
    public DbSet<Occurrence> Occurrences { get; set; }
    public DbSet<TitleHistory> TitleHistories { get; set; }
    public DbSet<FileImport> FileImports { get; set; }
    public DbSet<CollectionRule> CollectionRules { get; set; }
    public DbSet<Trigger> Triggers { get; set; }
    public DbSet<MessageTemplate> MessageTemplates { get; set; }
    public DbSet<Dispatch> Dispatches { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTime.UtcNow;
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
