namespace SmartCollect.Application.DTOs.Config;

public record DispatchWindowConfigResponse(
    bool Enabled,
    string TimeZone,
    string StartTime,
    string EndTime,
    bool PauseAutomaticDispatchDuringProcessing);
