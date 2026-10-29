using Pera.Application.Services;

namespace Pera.Api;

/// <summary>Runs reminders and escalations (AutomationService) every few minutes, each run in its own scope.</summary>
public sealed class AutomationWorker(IServiceScopeFactory scopes, ILogger<AutomationWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await Task.Delay(TimeSpan.FromSeconds(45), stop);
        while (!stop.IsCancellationRequested)
        {
            var minutes = 15;
            try
            {
                using var scope = scopes.CreateScope();
                minutes = Math.Clamp((await scope.ServiceProvider.GetRequiredService<ConfigService>().GetAsync(stop)).Alerts.WorkerMinutes, 1, 240);
                var r = await scope.ServiceProvider.GetRequiredService<AutomationService>().RunAsync(stop);
                if (r.Reminders + r.Escalations > 0) log.LogInformation("Automation: {reminders} reminder(s), {escalations} escalation(s)", r.Reminders, r.Escalations);
                if (r.Errors.Count > 0) log.LogWarning("Automation run finished with errors: {errors}", string.Join(" | ", r.Errors));
            }
            catch (Exception e) when (e is not OperationCanceledException) { log.LogError(e, "Automation run failed"); }
            await Task.Delay(TimeSpan.FromMinutes(minutes), stop);
        }
    }
}
