# InvoiceFlow API

ASP.NET Core Web API (.NET 10).

## Run locally

```bash
cd api/Invoiceflow.Api
dotnet run --launch-profile https
```

- Health check: `GET /api/health`
- API docs (development only): `/swagger`
## Database setup
dotnet tool restore
dotnet user-secrets set "ConnectionStrings:Default" "<your connection string>" --project api/Invoiceflow.Api
dotnet ef database update --project api/Invoiceflow.Api