using Microsoft.Extensions.DependencyInjection;
using Slims.Application.Services;

namespace Slims.Application;

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
        services.AddScoped<MasterService>();
        services.AddScoped<GrnService>();
        services.AddScoped<RequisitionService>();
        services.AddScoped<IssueService>();
        services.AddScoped<InstallationService>();
        services.AddScoped<ReturnService>();
        services.AddScoped<StockService>();
        services.AddScoped<ReportService>();
        services.AddScoped<LedgerService>();
        services.AddScoped<NotifySettingsService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<InboxService>();
        services.AddScoped<StoreControlService>();
        services.AddScoped<EvidenceService>();
        services.AddScoped<DeviceService>();
        services.AddScoped<AmendmentService>();
        services.AddScoped<ExploreService>();
        services.AddScoped<MobileService>();
        services.AddScoped<ScheduleService>();
        services.AddScoped<AutomationService>();
        return services;
    }
}
