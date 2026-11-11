namespace Invoiceflow.Api.Jobs;

// Runs inside the API: once at startup, then every hour
public class OverdueInvoicesJob(IServiceScopeFactory scopes, ILogger<OverdueInvoicesJob> logger) : BackgroundService
{
    static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                // A fresh scope (and database context) for every run, the same way each request gets one
                using var scope = scopes.CreateScope();
                var overdue = scope.ServiceProvider.GetRequiredService<OverdueInvoices>();
                var marked = await overdue.MarkAsync(DateTimeOffset.UtcNow, stoppingToken);

                // A count only: no client names or amounts in the logs
                if (marked > 0) logger.LogInformation("Marked {Count} invoice(s) overdue", marked);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A failed run (database down, say) must not take the API down. The next hour tries again.
                logger.LogError(ex, "Overdue invoice check failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}