namespace SmartCollect.Application.Interfaces;

using Microsoft.EntityFrameworkCore;
using SmartCollect.Domain.Entities;

public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<User> Users { get; }
    DbSet<Client> Clients { get; }
    DbSet<Contact> Contacts { get; }
    DbSet<Title> Titles { get; }
    DbSet<Occurrence> Occurrences { get; }
    DbSet<TitleHistory> TitleHistories { get; }
    DbSet<FileImport> FileImports { get; }
    DbSet<CollectionRule> CollectionRules { get; }
    DbSet<Trigger> Triggers { get; }
    DbSet<MessageTemplate> MessageTemplates { get; }
    DbSet<Dispatch> Dispatches { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
