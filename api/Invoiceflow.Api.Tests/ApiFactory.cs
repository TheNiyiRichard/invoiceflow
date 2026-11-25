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
using System.Security.Cryptography;
using Invoiceflow.Api.Mail;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Invoiceflow.Api.Payments;
using System.Text.RegularExpressions;

namespace Invoiceflow.Api.Tests;

// Starts the real API in memory against its own throwaway database
public class ApiFactory : WebApplicationFactory<Program>
{
    // Never the dev database. CI can point this elsewhere in Week 8.
    static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("INVOICEFLOW_TEST_DB")
        ?? "Server=localhost;Database=InvoiceflowTest;Trusted_Connection=True;TrustServerCertificate=True";

    public const string Password = "copper lantern harbour drift";
    readonly Dictionary<string, HttpClient> users = [];
    // Every email the API "sends" during the test run
    public FakeMailer Mailbox { get; } = new();
    // Stands in for Stripe: no real payments from tests
    public FakePayments Payments { get; } = new();

    // Tests sign their own webhooks with this, the way Stripe would
    public string WebhookSecret { get; } = $"whsec_test_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing", not "Development": no user-secrets (so no dev connection string) and no Swagger
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        // All test classes share one sign-in limiter, and Week 6 signs in a lot. The real app keeps 10 a minute.
        builder.UseSetting("RateLimits:AuthPerMinute", "300");
        // Re-check sessions on every request, so tests can see a reset sign everyone out straight away
        builder.UseSetting("Auth:SessionRecheckSeconds", "0");
        // A fresh signing key every run. Tests never see the real one.
        builder.UseSetting("PaymentLinks:Key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        builder.UseSetting("RateLimits:SendPerHour", "100");
        builder.UseSetting("RateLimits:PayPerMinute", "100");
        builder.UseSetting("Stripe:WebhookSecret", WebhookSecret);
        // No real email from tests: every message lands in Mailbox
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMailer>();
            services.AddSingleton<IMailer>(Mailbox);
            services.RemoveAll<IPaymentGateway>();
            services.AddSingleton<IPaymentGateway>(Payments);
        });
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
    // test would run into the sign-in limit, which all tests share.
    public async Task<HttpClient> SignedInUser(string tag)
    {
        if (users.TryGetValue(tag, out var existing)) return existing;
        var (browser, _) = await NewAccount(tag);
        users[tag] = browser;
        return browser;
    }

    // A brand-new account, signed in. Verified the real way (the link from the email) unless a test needs it unverified.
    public async Task<(HttpClient Browser, string Email)> NewAccount(string tag, bool verified = true)
    {
        var browser = NewBrowser();
        var email = $"{tag}-{Guid.NewGuid():N}@example.com";

        var register = await browser.PostAsJsonAsync("/api/auth/register", new { fullName = $"Tester {tag}", email, password = Password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        if (verified)
        {
            var (userId, code) = VerificationLink(Assert.Single(Mailbox.To(email)));
            var verify = await browser.PostAsJsonAsync("/api/auth/verify-email", new { userId, code });
            Assert.Equal(HttpStatusCode.NoContent, verify.StatusCode);
        }

        var login = await browser.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return (browser, email);
    }

    // The account id and code from a link in an account email
    public static (string UserId, string Code) VerificationLink(EmailMessage mail) => AccountLink(mail, "verify-email");
    public static (string UserId, string Code) ResetLink(EmailMessage mail) => AccountLink(mail, "reset-password");

    static (string UserId, string Code) AccountLink(EmailMessage mail, string page)
    {
        var m = Regex.Match(mail.Text, $@"/{page}\?user=([^&\s]+)&code=([A-Za-z0-9_-]+)");
        Assert.True(m.Success, $"No /{page} link in the email");
        return (Uri.UnescapeDataString(m.Groups[1].Value), m.Groups[2].Value);
    }

    // Jumps an invoice straight to a status, skipping email and payment, for tests that don't care how it got there
    public async Task SetStatus(Guid invoiceId, InvoiceStatus status)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Invoices.IgnoreQueryFilters().Where(i => i.Id == invoiceId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, status));
    }

     // As if the invoice was saved when the tax rate was different
    public async Task SetTaxRate(Guid invoiceId, decimal rate)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db.Invoices.IgnoreQueryFilters().Include(i => i.LineItems).SingleAsync(i => i.Id == invoiceId);
        invoice.TaxRate = rate;
        InvoiceMath.Recalculate(invoice);
        await db.SaveChangesAsync();
    }

    // As if the last reminder went out at this time
    public async Task SetLastReminded(Guid invoiceId, DateTime at)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Invoices.IgnoreQueryFilters().Where(i => i.Id == invoiceId)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.LastRemindedAt, (DateTime?)at));
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