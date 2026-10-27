using Invoiceflow.Api.Data;
using Microsoft.EntityFrameworkCore;
using Invoiceflow.Api.Models;
using Invoiceflow.Api.Auth;
var builder = WebApplication.CreateBuilder(args);

// Security: don't tell the world which web server we run
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' missing. Set it with dotnet user-secrets.");

builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
builder.Services.AddIdentityCore<ApplicationUser>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 15;
    o.Password.RequiredUniqueChars = 4;
    o.Password.RequireDigit = false;
    o.Password.RequireLowercase = false;
    o.Password.RequireUppercase = false;
    o.Password.RequireNonAlphanumeric = false;
})
    .AddEntityFrameworkStores<AppDbContext>()
    .AddPasswordValidator<CommonPasswordValidator>();
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
var app = builder.Build();

// API docs only on my own machine, never in production
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Invoiceflow API v1");
        options.DocumentTitle = "Invoiceflow API";
    });
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/api/health/db");
app.Run();