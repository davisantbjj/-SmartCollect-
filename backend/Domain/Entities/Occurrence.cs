namespace SmartCollect.Domain.Entities;

using SmartCollect.Domain.Common;
using SmartCollect.Domain.Enums;

public class Occurrence : Entity
{
    public Guid TitleId { get; set; }
    public TitleStatus UpdatedStatus { get; set; }
    public DateTime OccurrenceDate { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    public bool ThankYouSent { get; set; } = false;

    public Title Title { get; set; } = null!;
}
