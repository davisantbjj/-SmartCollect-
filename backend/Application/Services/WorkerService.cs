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

    public async Task<WorkerResponse?> UpdateAsync(Guid tenantId, Guid actorUserId, Guid workerId, UpdateWorkerRequest request)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new InvalidOperationException("Nome do usuário é obrigatório.");

        if (string.IsNullOrWhiteSpace(normalizedEmail))
            throw new InvalidOperationException("E-mail do usuário é obrigatório.");

        var worker = await _db.Users
            .FirstOrDefaultAsync(u =>
                u.Id == workerId &&
                u.TenantId == tenantId &&
                (u.Role == UserRole.Admin || u.Role == UserRole.Worker));

        if (worker is null) return null;

        var duplicatedEmail = await _db.Users.AnyAsync(u =>
            u.Id != workerId &&
            u.Email == normalizedEmail);

        if (duplicatedEmail)
            throw new InvalidOperationException("E-mail já cadastrado no sistema.");

        if (!request.Active && worker.Active)
        {
            if (actorUserId == worker.Id)
                throw new InvalidOperationException("Você não pode excluir ou inativar o próprio usuário.");

            if (worker.Role == UserRole.Admin)
            {
                var activeAdminCount = await _db.Users.CountAsync(u =>
                    u.TenantId == tenantId &&
                    u.Role == UserRole.Admin &&
                    u.Active);

                if (activeAdminCount <= 1)
                    throw new InvalidOperationException("Não é possível excluir ou inativar o último administrador da empresa.");
            }
        }

        worker.Name = request.Name.Trim();
        worker.Email = normalizedEmail;
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

    public async Task<bool> DeleteAsync(Guid tenantId, Guid workerId)
    {
        var worker = await _db.Users
            .FirstOrDefaultAsync(u =>
                u.Id == workerId &&
                u.TenantId == tenantId &&
                (u.Role == UserRole.Admin || u.Role == UserRole.Worker));

        if (worker is null) return false;

        if (worker.Active)
            throw new InvalidOperationException("Somente usuarios inativos podem ser excluidos definitivamente.");

        var hasLinkedClients = await _db.Clients.AnyAsync(c => c.UserId == workerId);
        if (hasLinkedClients)
            throw new InvalidOperationException("Nao e possivel excluir este usuario porque ele possui clientes vinculados.");

        _db.Users.Remove(worker);
        await _db.SaveChangesAsync();
        return true;
    }
}
