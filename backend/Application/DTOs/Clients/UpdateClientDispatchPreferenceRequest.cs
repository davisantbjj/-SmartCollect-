namespace SmartCollect.Application.DTOs.Clients;

public record UpdateClientDispatchPreferenceRequest(
    bool? SendToAllContacts,
    string? DispatchMode,
    List<Guid>? SelectedContactIds
);
