namespace SmartCollect.Application.Interfaces;

using SmartCollect.Application.DTOs.Users;

public interface IWorkerService
{
    Task<List<WorkerResponse>> ListAsync(Guid tenantId);
    Task<WorkerResponse?> UpdateAsync(Guid tenantId, Guid actorUserId, Guid workerId, UpdateWorkerRequest request);
    Task<bool> DeleteAsync(Guid tenantId, Guid workerId);
}
