namespace SmartCollect.Infrastructure;

using System.Net.Http.Headers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;
using SmartCollect.Application.Interfaces;
using SmartCollect.Application.Services;
using SmartCollect.Domain.Interfaces;
using SmartCollect.Infrastructure.Data;
using SmartCollect.Infrastructure.Repositories;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly("SmartCollect.Api")));

        var baseUrl = Environment.GetEnvironmentVariable("EXTERNAL_API_BASE_URL")
            ?? configuration["ExternalApi:BaseUrl"]
            ?? "https://api-mock.atoscapital.com.br/v1/";

        var bearerToken = Environment.GetEnvironmentVariable("EXTERNAL_API_TOKEN")
            ?? configuration["ExternalApi:Token"];

        services.AddHttpClient("ExternalSyncApi", client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (!string.IsNullOrWhiteSpace(bearerToken))
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
        })
        .AddPolicyHandler(HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))));

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ITitleService, TitleService>();
        services.AddScoped<IClientService, ClientService>();
        services.AddScoped<IContactService, ContactService>();
        services.AddScoped<ICollectionRuleService, CollectionRuleService>();
        services.AddScoped<IMessageTemplateService, MessageTemplateService>();
        services.AddScoped<IFileImportService, FileImportService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ISmtpConfigService, SmtpConfigService>();
        services.AddScoped<IDispatchDeliveryService, DispatchDeliveryService>();
        services.AddScoped<ISyncService, SyncService>();
        services.AddScoped<ITenantService, TenantService>(); // B-08 - Master access
        services.AddScoped<IWorkerService, WorkerService>();

        return services;
    }
}
