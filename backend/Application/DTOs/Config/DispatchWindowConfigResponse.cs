namespace SmartCollect.Application.DTOs.Config;

public record DispatchWindowConfigResponse(
    bool Enabled,
    string TimeZone,
    string StartTime,
    string EndTime,
    List<int> DaysOfWeek,
    bool PauseAutomaticDispatchDuringProcessing);
