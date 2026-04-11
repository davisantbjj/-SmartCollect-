namespace SmartCollect.Domain.Entities;

using SmartCollect.Domain.Common;

public class Client : Entity, ITenantScoped
{
    public Guid UserId { get; set; }
    public Guid TenantId { get; set; }
    public string LegalName { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public string? TradeName { get; set; }

    public User User { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();
    public ICollection<Title> Titles { get; set; } = new List<Title>();
}
