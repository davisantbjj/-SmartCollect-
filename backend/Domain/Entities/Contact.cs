namespace SmartCollect.Domain.Entities;

using SmartCollect.Domain.Common;
using SmartCollect.Domain.Enums;

public class Contact : Entity
{
    public Guid ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? WhatsAppPhone { get; set; }
    public ContactDepartment Department { get; set; } = ContactDepartment.Finance;
    public bool IsPrimary { get; set; } = false;

    public Client Client { get; set; } = null!;
    public ICollection<Dispatch> Dispatches { get; set; } = new List<Dispatch>();
}
