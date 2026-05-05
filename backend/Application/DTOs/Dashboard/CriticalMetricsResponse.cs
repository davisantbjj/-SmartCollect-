namespace SmartCollect.Application.DTOs.Dashboard;

public record RecoveryRatePointResponse(
    string Month,
    int OverdueBaseTitles,
    int RecoveredTitles,
    double RecoveryRate
);

public record CriticalMetricsResponse(
    int CriticalTitles,
    int OverdueBaseTitles,
    int RecoveredTitles,
    double RecoveryRate,
    List<RecoveryRatePointResponse> Trend
);
