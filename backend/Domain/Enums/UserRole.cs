namespace SmartCollect.Domain.Enums;

public enum UserRole
{
    Master,   // Atos Capital — acesso cross-tenant
    Admin,    // Cliente SaaS — gerencia próprio tenant
    Worker    // Usuário operacional — acesso restrito
}
