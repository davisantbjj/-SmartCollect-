namespace SmartCollect.Application.DTOs.Config;

using System.ComponentModel.DataAnnotations;

public class DispatchWindowConfigRequest
{
    public bool Enabled { get; set; }

    public string? TimeZone { get; set; }

    [Required]
    public string StartTime { get; set; } = "09:00";

    [Required]
    public string EndTime { get; set; } = "18:00";

    public bool PauseAutomaticDispatchDuringProcessing { get; set; } = true;
}
