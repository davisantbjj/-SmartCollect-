namespace SmartCollect.Application.DTOs.Config;

using System.ComponentModel.DataAnnotations;

public class ExternalApiConfigRequest
{
    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    public string? DocsUrl { get; set; }

    [Required]
    public string PendingTitlesPath { get; set; } = "titulos-pendentes";

    [Required]
    public string OccurrencesPath { get; set; } = "ocorrencias?data={date}";

    [Required]
    public string AuthenticationScheme { get; set; } = "Bearer";

    public string? Token { get; set; }

    public bool ClearToken { get; set; }
}
