namespace SmartCollect.Domain.Entities;

using SmartCollect.Domain.Common;
using SmartCollect.Domain.Enums;

public class Tenant : Entity
{
    public string CompanyName { get; set; } = string.Empty;
    public string TaxId { get; set; } = string.Empty;
    public string EmailDomain { get; set; } = string.Empty;
    public string? SmtpHost { get; set; }
    public int? SmtpPort { get; set; }
    public string? SmtpUser { get; set; }
    public string? SmtpPasswordEncrypted { get; set; }
    public string? ExternalApiBaseUrl { get; set; }
    public string? ExternalApiDocsUrl { get; set; }
    public string? ExternalApiPendingTitlesPath { get; set; }
    public string? ExternalApiOccurrencesPath { get; set; }
    public string? ExternalApiAuthScheme { get; set; }
    public string? ExternalApiTokenEncrypted { get; set; }
    public string? WhatsAppApiToken { get; set; }
    public bool DispatchWindowEnabled { get; set; }
    public int DispatchWindowStartMinutes { get; set; } = 540;
    public int DispatchWindowEndMinutes { get; set; } = 1080;
    public string DispatchWindowTimeZone { get; set; } = "UTC";
    public bool PauseAutomaticDispatchDuringProcessing { get; set; } = true;
    public TenantPlan Plan { get; set; } = TenantPlan.Basic;
    public bool Active { get; set; } = true;

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<CollectionRule> CollectionRules { get; set; } = new List<CollectionRule>();
    public ICollection<FileImport> FileImports { get; set; } = new List<FileImport>();
    public ICollection<MessageTemplate> MessageTemplates { get; set; } = new List<MessageTemplate>();
}
