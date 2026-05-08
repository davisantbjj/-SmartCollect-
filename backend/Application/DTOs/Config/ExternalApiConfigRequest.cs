namespace SmartCollect.Application.DTOs.Config;

using System.ComponentModel.DataAnnotations;

public class ExternalApiConfigRequest
{
    [Required]
    public string BaseUrl { get; set; } = string.Empty;

    public string? DocsUrl { get; set; }

    [Required]
    public string PendingTitlesPath { get; set; } = "reguacobranca?colecao=1&pageSize=0&pageNumber=0";

    [Required]
    public string OccurrencesPath { get; set; } = "reguacobranca?colecao=2&dtOcorrencia={date}&pageSize=0&pageNumber=0";

    [Required]
    public string AuthenticationScheme { get; set; } = "Bearer";

    public string? Token { get; set; }

    public bool ClearToken { get; set; }
}
