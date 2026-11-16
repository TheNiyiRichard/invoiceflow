using System.Net;
using Invoiceflow.Api.Jobs;

namespace Invoiceflow.Api.Tests;

// The heartbeat on its own, with a clock the test controls
public class JobHeartbeatTests
{
    sealed class TestClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    static readonly DateTimeOffset Start = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Healthy_only_while_runs_keep_succeeding()
    {
        var clock = new TestClock(Start);
        var heartbeat = new JobHeartbeat(clock);
        heartbeat.Beat();

        clock.Now = Start.AddHours(2);
        Assert.True(heartbeat.IsHealthy());

        clock.Now = Start.AddHours(2).AddMinutes(1);
        Assert.False(heartbeat.IsHealthy());

        heartbeat.Beat(); // the job comes back
        Assert.True(heartbeat.IsHealthy());
    }

    [Fact]
    public void A_job_that_never_succeeds_goes_unhealthy_2_hours_after_startup()
    {
        var clock = new TestClock(Start);
        var heartbeat = new JobHeartbeat(clock);
        Assert.True(heartbeat.IsHealthy()); // just started: the first run is on its way

        clock.Now = Start.AddHours(2).AddMinutes(1);
        Assert.False(heartbeat.IsHealthy());
    }
}

// The real endpoints, signed out, the way an uptime monitor calls them
[Collection("api")]
public class HealthEndpointTests(ApiFactory factory)
{
    [Theory]
    [InlineData("/api/health/db")]
    [InlineData("/api/health/jobs")]
    public async Task Health_checks_answer_signed_out_with_one_word(string url)
    {
        var res = await factory.NewBrowser().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("Healthy", await res.Content.ReadAsStringAsync());
    }
}