using Invoiceflow.Api.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Invoiceflow.Api.Tests;

// Starts the real API in memory against its own throwaway database
public class ApiFactory : WebApplicationFactory<Program>
{
    // Never the dev database. CI can point this elsewhere in Week 8.
    static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("INVOICEFLOW_TEST_DB")
        ?? "Server=localhost;Database=InvoiceflowTest;Trusted_Connection=True;TrustServerCertificate=True";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing", not "Development": no user-secrets (so no dev connection string) and no Swagger
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
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
}