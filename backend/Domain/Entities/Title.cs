namespace SmartCollect.Domain.Entities;

using SmartCollect.Domain.Common;
using SmartCollect.Domain.Enums;

public class Title : Entity, ITenantScoped
{
    public Guid ClientId { get; set; }
    public Guid TenantId { get; set; }
    public Guid? ImportId { get; set; }
    public string UniqueCode { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime IssueDate { get; set; }
    public string? BoletoUrl { get; set; }
    public TitleStatus Status { get; set; } = TitleStatus.Open;

    public Client Client { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
    public FileImport? Import { get; set; }
    public ICollection<Occurrence> Occurrences { get; set; } = new List<Occurrence>();
    public ICollection<Dispatch> Dispatches { get; set; } = new List<Dispatch>();
    public ICollection<TitleHistory> Histories { get; set; } = new List<TitleHistory>();
}
