using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Invoiceflow.Api.Jobs;

// Remembers when the overdue job last finished a run. One shared instance for the whole app.
public class JobHeartbeat(TimeProvider clock)
{
    public static readonly TimeSpan MaxSilence = TimeSpan.FromHours(2);

    readonly DateTimeOffset started = clock.GetUtcNow();
    long lastSuccessTicks; // 0 until the first run succeeds

    public void Beat() => Interlocked.Exchange(ref lastSuccessTicks, clock.GetUtcNow().UtcTicks);

    // Healthy only if a run succeeded in the last 2 hours. A job that has never succeeded
    // gets 2 hours from startup, then counts as stopped: "never ran" must not look like "all fine".
    public bool IsHealthy()
    {
        var ticks = Interlocked.Read(ref lastSuccessTicks);
        var since = ticks == 0 ? started : new DateTimeOffset(ticks, TimeSpan.Zero);
        return clock.GetUtcNow() - since <= MaxSilence;
    }
}

// Answers /api/health/jobs with one word. No times, counts or error messages.
public class JobsHealthCheck(JobHeartbeat heartbeat) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default) =>
        Task.FromResult(heartbeat.IsHealthy() ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy());
}