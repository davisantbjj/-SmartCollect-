namespace SmartCollect.Domain.Entities;

using SmartCollect.Domain.Common;
using SmartCollect.Domain.Enums;

public class Trigger : Entity
{
    public Guid CollectionRuleId { get; set; }
    public Guid TemplateId { get; set; }
    public CollectionChannel Channel { get; set; } = CollectionChannel.Email;
    public int DaysOffset { get; set; }
    public TriggerReference Reference { get; set; } = TriggerReference.DueDate;
    public int Order { get; set; }
    public bool Active { get; set; } = true;

    public CollectionRule CollectionRule { get; set; } = null!;
    public MessageTemplate Template { get; set; } = null!;
    public ICollection<Dispatch> Dispatches { get; set; } = new List<Dispatch>();
}
