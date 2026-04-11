namespace SmartCollect.Application.Interfaces;

using SmartCollect.Application.DTOs.Users;

public interface IWorkerService
{
    Task<List<WorkerResponse>> ListAsync(Guid tenantId);
    Task<WorkerResponse?> UpdateAsync(Guid tenantId, Guid workerId, UpdateWorkerRequest request);
}
