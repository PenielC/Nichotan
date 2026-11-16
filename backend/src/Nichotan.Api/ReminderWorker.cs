using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nichotan.Application.Common;
using Nichotan.Application.Reminders;

namespace Nichotan.Api;

/// <summary>
/// Runs in the background of the API and asks <see cref="ReminderService"/> what is due, every few minutes. A failed run is logged
/// and tried again on the next tick; it never stops the API. It does no work of its own, so the same logic runs in tests with a chosen time.
/// </summary>
public class ReminderWorker(IServiceScopeFactory scopes, IClock clock, IOptions<ReminderOptions> options, ILogger<ReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("The reminder worker is switched off");
            return;
        }

        var every = TimeSpan.FromMinutes(Math.Max(1, options.Value.RunEveryMinutes));
        logger.LogInformation("The reminder worker runs every {Minutes} minutes", every.TotalMinutes);
        try
        {
            // A short pause first, so startup isn't competing with a run.
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            using var timer = new PeriodicTimer(every);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<ReminderService>().RunAsync(clock.UtcNow, cancellationToken);
            if (result.StudentReminders + result.StaffDigests > 0)
            {
                logger.LogInformation("Reminders sent: {Students} to students, {Digests} staff digests", result.StudentReminders, result.StaffDigests);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A reminder run failed; it will be tried again on the next tick");
        }
    }
}
