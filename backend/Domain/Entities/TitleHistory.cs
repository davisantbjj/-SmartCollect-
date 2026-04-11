namespace SmartCollect.Domain.Entities;

using SmartCollect.Domain.Common;

public class TitleHistory : Entity, ITenantScoped
{
    public Guid TitleId { get; set; }
    public Guid TenantId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public Title Title { get; set; } = null!;
}
