using System.Net;
using System.Net.Http.Json;
using Invoiceflow.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Invoiceflow.Api.Models;
using Invoiceflow.Api.Jobs;

namespace Invoiceflow.Api.Tests;

// Starts the real API in memory against its own throwaway database
public class ApiFactory : WebApplicationFactory<Program>
{
    // Never the dev database. CI can point this elsewhere in Week 8.
    static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("INVOICEFLOW_TEST_DB")
        ?? "Server=localhost;Database=InvoiceflowTest;Trusted_Connection=True;TrustServerCertificate=True";

    const string Password = "copper lantern harbour drift";
    readonly Dictionary<string, HttpClient> users = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing", not "Development": no user-secrets (so no dev connection string) and no Swagger
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        // All test classes share one sign-in limiter. The real app keeps 10 a minute.
        builder.UseSetting("RateLimits:AuthPerMinute", "100");
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // EnsureDeleted wipes the database, so refuse anything that isn't clearly a test database
        var name = db.Database.GetDbConnection().Database;
        if (!name.Contains("Test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to reset '{name}': not a test database.");

        db.Database.EnsureDeleted(); // fresh every run
        db.Database.Migrate();       // built from the real migrations
        return host;
    }

    // Each one has its own cookie jar, like a separate browser
    public HttpClient NewBrowser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"), // the auth cookie is Secure-only
        AllowAutoRedirect = false
    });

    // One signed-in browser per tag ("a", "b"), shared by every test. New users in every
    // test would run into the 10-a-minute sign-in limit, which all tests share.
    public async Task<HttpClient> SignedInUser(string tag)
    {
        if (users.TryGetValue(tag, out var existing)) return existing;

        var browser = NewBrowser();
        var email = $"{tag}-{Guid.NewGuid():N}@example.com";

        var register = await browser.PostAsJsonAsync("/api/auth/register", new { fullName = $"Tester {tag}", email, password = Password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var login = await browser.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        users[tag] = browser;
        return browser;
    }

     // There's no "send" endpoint yet (Day 30), so tests move an invoice out of Draft directly in the test database
    public async Task SetStatus(Guid invoiceId, InvoiceStatus status)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Invoices.IgnoreQueryFilters().Where(i => i.Id == invoiceId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, status));
    }

     // Runs the overdue check once, as if it were this moment
    public async Task<int> RunOverdueJob(DateTimeOffset now)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<OverdueInvoices>().MarkAsync(now);
    }

    // There's no payment yet (Week 5), so tests mark an invoice paid directly in the test database
    public async Task MarkPaid(Guid invoiceId, DateOnly issued, DateTime paidAt)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Invoices.IgnoreQueryFilters().Where(i => i.Id == invoiceId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.Status, InvoiceStatus.Paid)
                .SetProperty(i => i.IssueDate, issued)
                .SetProperty(i => i.PaidAt, (DateTime?)paidAt));
    }
}