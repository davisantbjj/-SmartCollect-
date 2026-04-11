namespace SmartCollect.Domain.Entities;

using SmartCollect.Domain.Common;
using SmartCollect.Domain.Enums;

public class Dispatch : Entity
{
    public Guid TitleId { get; set; }
    public Guid ContactId { get; set; }
    public Guid TriggerId { get; set; }
    public CollectionChannel Channel { get; set; } = CollectionChannel.Email;
    public DispatchStatus Status { get; set; } = DispatchStatus.Pending;
    public DateTime ScheduledFor { get; set; }
    public DateTime? SentAt { get; set; }

    public Title Title { get; set; } = null!;
    public Contact Contact { get; set; } = null!;
    public Trigger Trigger { get; set; } = null!;
}
