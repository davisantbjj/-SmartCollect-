namespace SmartCollect.Application.Services;

using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.Users;
using SmartCollect.Application.Interfaces;
using SmartCollect.Domain.Enums;

public class WorkerService : IWorkerService
{
    private readonly IAppDbContext _db;

    public WorkerService(IAppDbContext db) => _db = db;

    public async Task<List<WorkerResponse>> ListAsync(Guid tenantId)
    {
        return await _db.Users
            .Where(u =>
                u.TenantId == tenantId &&
                (u.Role == UserRole.Admin || u.Role == UserRole.Worker))
            .OrderByDescending(u => u.Role == UserRole.Admin)
            .ThenBy(u => u.Name)
            .Select(u => new WorkerResponse(
                u.Id,
                u.Name,
                u.Email,
                u.Role.ToString(),
                u.Active,
                u.CreatedAt,
                u.LastLogin))
            .ToListAsync();
    }

    public async Task<WorkerResponse?> UpdateAsync(Guid tenantId, Guid workerId, UpdateWorkerRequest request)
    {
        var worker = await _db.Users
            .FirstOrDefaultAsync(u =>
                u.Id == workerId &&
                u.TenantId == tenantId &&
                (u.Role == UserRole.Admin || u.Role == UserRole.Worker));

        if (worker is null) return null;

        worker.Name = request.Name.Trim();
        worker.Active = request.Active;

        if (!string.IsNullOrWhiteSpace(request.Password))
            worker.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

        await _db.SaveChangesAsync();

        return new WorkerResponse(
            worker.Id,
            worker.Name,
            worker.Email,
            worker.Role.ToString(),
            worker.Active,
            worker.CreatedAt,
            worker.LastLogin);
    }
}
