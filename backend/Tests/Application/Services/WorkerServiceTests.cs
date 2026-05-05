using SmartCollect.Tests;

namespace SmartCollect.Tests.Application.Services;

using Microsoft.EntityFrameworkCore;
using SmartCollect.Application.DTOs.Users;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Entities;
using SmartCollect.Domain.Enums;

public class WorkerServiceTests
{
    [Fact]
    public async Task UpdateAsync_UpdatesEmailSuccessfully()
    {
        var db = TestDbContextFactory.Create();
        var tenant = new Tenant { Id = Guid.NewGuid(), CompanyName = "Test", TaxId = "123" };
        var admin = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Admin",
            Email = "admin@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Role = UserRole.Admin,
            Active = true
        };
        var worker = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Worker",
            Email = "worker@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Role = UserRole.Worker,
            Active = true
        };
        db.Tenants.Add(tenant);
        db.Users.AddRange(admin, worker);
        await db.SaveChangesAsync();

        var service = new WorkerService(db);
        var result = await service.UpdateAsync(
            tenant.Id,
            admin.Id,
            worker.Id,
            new UpdateWorkerRequest("Worker Updated", "updated@test.com", true, null));

        Assert.NotNull(result);
        Assert.Equal("updated@test.com", result!.Email);
        Assert.Equal("Worker Updated", result.Name);
    }

    [Fact]
    public async Task UpdateAsync_DuplicateGlobalEmail_Throws()
    {
        var db = TestDbContextFactory.Create();
        var tenantA = new Tenant { Id = Guid.NewGuid(), CompanyName = "A", TaxId = "123" };
        var tenantB = new Tenant { Id = Guid.NewGuid(), CompanyName = "B", TaxId = "456" };
        var admin = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA.Id,
            Name = "Admin",
            Email = "admin@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Role = UserRole.Admin,
            Active = true
        };
        var worker = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantA.Id,
            Name = "Worker",
            Email = "worker@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Role = UserRole.Worker,
            Active = true
        };
        var otherTenantUser = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenantB.Id,
            Name = "Other",
            Email = "shared@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Role = UserRole.Admin,
            Active = true
        };
        db.Tenants.AddRange(tenantA, tenantB);
        db.Users.AddRange(admin, worker, otherTenantUser);
        await db.SaveChangesAsync();

        var service = new WorkerService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(
                tenantA.Id,
                admin.Id,
                worker.Id,
                new UpdateWorkerRequest("Worker", "shared@test.com", true, null)));

        Assert.Equal("E-mail já cadastrado no sistema.", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_CannotDeactivateOwnUser()
    {
        var db = TestDbContextFactory.Create();
        var tenant = new Tenant { Id = Guid.NewGuid(), CompanyName = "Test", TaxId = "123" };
        var admin = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Admin",
            Email = "admin@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Role = UserRole.Admin,
            Active = true
        };
        db.Tenants.Add(tenant);
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var service = new WorkerService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(
                tenant.Id,
                admin.Id,
                admin.Id,
                new UpdateWorkerRequest("Admin", "admin@test.com", false, null)));

        Assert.Equal("Você não pode excluir ou inativar o próprio usuário.", ex.Message);
    }

    [Fact]
    public async Task UpdateAsync_CannotDeactivateLastActiveAdmin()
    {
        var db = TestDbContextFactory.Create();
        var tenant = new Tenant { Id = Guid.NewGuid(), CompanyName = "Test", TaxId = "123" };
        var admin = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Admin",
            Email = "admin@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Role = UserRole.Admin,
            Active = true
        };
        var worker = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Worker",
            Email = "worker@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Role = UserRole.Worker,
            Active = true
        };
        db.Tenants.Add(tenant);
        db.Users.AddRange(admin, worker);
        await db.SaveChangesAsync();

        var service = new WorkerService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.UpdateAsync(
                tenant.Id,
                worker.Id,
                admin.Id,
                new UpdateWorkerRequest("Admin", "admin@test.com", false, null)));

        Assert.Equal("Não é possível excluir ou inativar o último administrador da empresa.", ex.Message);
    }
    [Fact]
    public async Task DeleteAsync_RemovesInactiveUser()
    {
        var db = TestDbContextFactory.Create();
        var tenant = new Tenant { Id = Guid.NewGuid(), CompanyName = "Test", TaxId = "123" };
        var worker = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Worker",
            Email = "worker@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Role = UserRole.Worker,
            Active = false
        };
        db.Tenants.Add(tenant);
        db.Users.Add(worker);
        await db.SaveChangesAsync();

        var service = new WorkerService(db);
        var deleted = await service.DeleteAsync(tenant.Id, worker.Id);

        Assert.True(deleted);
        Assert.False(await db.Users.AnyAsync(u => u.Id == worker.Id));
    }

    [Fact]
    public async Task DeleteAsync_ActiveUser_Throws()
    {
        var db = TestDbContextFactory.Create();
        var tenant = new Tenant { Id = Guid.NewGuid(), CompanyName = "Test", TaxId = "123" };
        var worker = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Worker",
            Email = "worker@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Role = UserRole.Worker,
            Active = true
        };
        db.Tenants.Add(tenant);
        db.Users.Add(worker);
        await db.SaveChangesAsync();

        var service = new WorkerService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteAsync(tenant.Id, worker.Id));

        Assert.Equal("Somente usuarios inativos podem ser excluidos definitivamente.", ex.Message);
    }

    [Fact]
    public async Task DeleteAsync_UserWithLinkedClients_Throws()
    {
        var db = TestDbContextFactory.Create();
        var tenant = new Tenant { Id = Guid.NewGuid(), CompanyName = "Test", TaxId = "123" };
        var worker = new User
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Name = "Worker",
            Email = "worker@test.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Role = UserRole.Worker,
            Active = false
        };
        var client = new Client
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            UserId = worker.Id,
            LegalName = "Cliente",
            TaxId = "123456789"
        };
        db.Tenants.Add(tenant);
        db.Users.Add(worker);
        db.Clients.Add(client);
        await db.SaveChangesAsync();

        var service = new WorkerService(db);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteAsync(tenant.Id, worker.Id));

        Assert.Equal("Nao e possivel excluir este usuario porque ele possui clientes vinculados.", ex.Message);
    }
}
