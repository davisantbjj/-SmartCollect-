namespace SmartCollect.Domain.Entities;

using SmartCollect.Domain.Common;

public class CollectionRule : Entity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Active { get; set; } = false;

    public Tenant Tenant { get; set; } = null!;
    public ICollection<Trigger> Triggers { get; set; } = new List<Trigger>();
}
