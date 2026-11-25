using System.Threading.RateLimiting;
using Invoiceflow.Api.Auth;
using Invoiceflow.Api.Data;
using Invoiceflow.Api.Jobs;
using Invoiceflow.Api.Models;
using Invoiceflow.Api.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Invoiceflow.Api.Mail;

var builder = WebApplication.CreateBuilder(args);
// QuestPDF: free Community licence (individuals and businesses under $1M a year)
QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

// Security: don't tell the world which web server we run
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' missing. Set it with dotnet user-secrets.");

// Signs payment links. A secret, so it lives in user-secrets like the connection string, never in git.
var paymentLinkKey = PaymentLinks.ReadKey(builder.Configuration["PaymentLinks:Key"]);
var webUrl = builder.Configuration["App:WebUrl"]
    ?? throw new InvalidOperationException("App:WebUrl missing. Set it in appsettings.json.");

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));

builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();

builder.Services.AddAuthorization(o =>
{
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    // Emailing someone else (invoices, reminders) also needs a verified email address
    o.AddPolicy(VerifiedEmailRequirement.Policy, p => p.RequireAuthenticatedUser().AddRequirements(new VerifiedEmailRequirement()));
});
builder.Services.AddScoped<IAuthorizationHandler, VerifiedEmailHandler>();

builder.Services.AddIdentityCore<ApplicationUser>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 15;
    o.Password.RequiredUniqueChars = 4;
    o.Password.RequireDigit = false;
    o.Password.RequireLowercase = false;
    o.Password.RequireUppercase = false;
    o.Password.RequireNonAlphanumeric = false;

    o.Lockout.AllowedForNewUsers = true;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    // Reset links use their own provider (1 hour), not the default one (24 hours)
    o.Tokens.PasswordResetTokenProvider = PasswordResetTokenOptions.Provider;
})
    .AddEntityFrameworkStores<AppDbContext>()
    .AddPasswordValidator<CommonPasswordValidator>()
    .AddSignInManager()
    .AddDefaultTokenProviders() // email verification links (24 hours)
    .AddTokenProvider<PasswordResetTokenProvider>(PasswordResetTokenOptions.Provider); // password reset links (1 hour)

// Verification links work for 24 hours. Each one is tied to its account, so it can't verify anyone else.
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(AccountEmail.LinkHours));

// Each signed-in browser re-checks its account once a minute. A password reset changes the account's
// security stamp, so every old session is signed out within a minute. (Tests re-check on every request.)
builder.Services.Configure<SecurityStampValidatorOptions>(o =>
    o.ValidationInterval = TimeSpan.FromSeconds(builder.Configuration.GetValue("Auth:SessionRecheckSeconds", 60)));

builder.Services.ConfigureApplicationCookie(o =>
{
    o.Cookie.Name = "__Host-invoiceflow";
    o.Cookie.HttpOnly = true;
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = false;
    // An API answers 401/403 instead of redirecting to a login page
    o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
    o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
});

// Per IP address, per minute. Tests raise the sign-in limit, see ApiFactory.
var authPerMinute = builder.Configuration.GetValue("RateLimits:AuthPerMinute", 10);
var payPerMinute = builder.Configuration.GetValue("RateLimits:PayPerMinute", 30);
var sendPerHour = builder.Configuration.GetValue("RateLimits:SendPerHour", 20);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => PerIp(ctx, authPerMinute)); // login and register
    o.AddPolicy("pay", ctx => PerIp(ctx, payPerMinute));   // public payment links
    // Sending email: per signed-in user, so nobody can use Invoiceflow to spam people
    o.AddPolicy("send", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "signed-out",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = sendPerHour, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
});

static RateLimitPartition<string> PerIp(HttpContext ctx, int perMinute) =>
    RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = perMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 });

// Tagged, so each URL runs only its own check: a stopped job never makes the database look down
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>(tags: ["db"])
    .AddCheck<JobsHealthCheck>("jobs", tags: ["jobs"]);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<JobHeartbeat>();
builder.Services.AddSingleton(sp => new PaymentLinks(paymentLinkKey, webUrl, sp.GetRequiredService<TimeProvider>()));
// Email over SMTP. In development that's a Mailtrap sandbox, so nothing reaches a real inbox.
builder.Services.Configure<SmtpSettings>(builder.Configuration.GetSection("Smtp"));
builder.Services.AddSingleton<IMailer, SmtpMailer>();
// Card payments through Stripe: test keys only for now (user-secrets)
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
builder.Services.AddSingleton<IPaymentGateway, StripeGateway>();
// Checks Stripe's webhook signatures. Not replaced in tests: they sign their own webhooks, so the real check runs.
builder.Services.AddSingleton<StripeWebhooks>();
builder.Services.AddScoped<OverdueInvoices>();
// Tests run the check themselves with a fixed clock, so the hourly timer stays off there
if (!builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddHostedService<OverdueInvoicesJob>();

var app = builder.Build();

// API docs only on my own machine, never in production
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous(); // /openapi/v1.json
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/openapi/v1.json", "Invoiceflow API v1");
        options.DocumentTitle = "Invoiceflow API";
    });
}

app.UseHttpsRedirection();
app.UseAuthentication(); // first, so the send limit can tell users apart
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
// One word each (Healthy/Unhealthy) and no details. Signed out, because uptime monitors are.
app.MapHealthChecks("/api/health/db", new HealthCheckOptions { Predicate = c => c.Tags.Contains("db") }).AllowAnonymous();
app.MapHealthChecks("/api/health/jobs", new HealthCheckOptions { Predicate = c => c.Tags.Contains("jobs") }).AllowAnonymous();
app.Run();