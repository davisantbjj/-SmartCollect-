namespace SmartCollect.Domain.Entities;

using SmartCollect.Domain.Common;
using SmartCollect.Domain.Enums;

public class FileImport : Entity, ITenantScoped
{
    public Guid TenantId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public ImportType Type { get; set; } = ImportType.Excel;
    public ImportStatus Status { get; set; } = ImportStatus.Processing;
    public int TotalRows { get; set; }
    public int SuccessRows { get; set; }
    public int ErrorRows { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public ICollection<Title> Titles { get; set; } = new List<Title>();
}
