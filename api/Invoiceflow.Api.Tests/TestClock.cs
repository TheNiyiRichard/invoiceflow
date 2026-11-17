namespace Invoiceflow.Api.Tests;

// A clock the test moves by hand
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
}