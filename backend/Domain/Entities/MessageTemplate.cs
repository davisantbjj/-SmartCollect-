namespace SmartCollect.Domain.Entities;

using SmartCollect.Domain.Common;
using SmartCollect.Domain.Enums;

public class MessageTemplate : Entity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public CollectionChannel Channel { get; set; } = CollectionChannel.Email;
    public string? Subject { get; set; }
    public string Body { get; set; } = string.Empty;
    public TemplateType Type { get; set; } = TemplateType.Collection;
    public bool Active { get; set; } = true;

    public Tenant Tenant { get; set; } = null!;
    public ICollection<Trigger> Triggers { get; set; } = new List<Trigger>();
}
