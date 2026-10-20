var builder = WebApplication.CreateBuilder(args);

// Security: don't tell the world which web server we run
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddControllers();
builder.Services.AddOpenApi();

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

app.Run();