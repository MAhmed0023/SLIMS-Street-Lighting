using Microsoft.Extensions.DependencyInjection;
using Pera.Application.Services;

namespace Pera.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ConfigService>();
        services.AddScoped<AuditService>();
        services.AddScoped<UserContext>();
        services.AddScoped<NumberingService>();
        services.AddScoped<AuthService>();
        services.AddScoped<UsersService>();
        services.AddScoped<HierarchyService>();
        services.AddScoped<OrganizationService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<FileService>();
        services.AddScoped<TaskService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<WorkflowAdminService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<ReportService>();
        services.AddScoped<AutomationService>();
        return services;
    }
}
