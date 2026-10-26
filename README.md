# InvoiceFlow

Invoicing for freelancers, with security built in from day one.

Create invoices, send them with signed payment links, get paid by card, and see every action in an audit log.

> 🚧 Building in public: 8 weeks, one commit a day (Oct 19 – Dec 13, 2026).

## Stack

- **API:** ASP.NET Core (.NET 10), Entity Framework Core
- **Web:** React + Vite
- **Database:** SQL Server
- **Payments:** Stripe (test mode)

## Security features (planned)

- [ ] Secure cookie sessions with account lockout
- [ ] Two-factor sign-in (TOTP) with recovery codes
- [ ] Signed, expiring payment links (HMAC-SHA256)
- [ ] Verified Stripe webhooks
- [ ] Tamper-evident audit log
- [ ] Role-based access (Owner, Accountant, Viewer)
- [ ] Encryption of sensitive fields at rest
- [ ] Security headers, CSP and rate limiting

## Project structure

- `api/`: ASP.NET Core Web API
- `web/`: React front end

## Run it locally

**You need:** .NET 10 SDK · Node.js LTS · SQL Server 2022 (Developer edition)

**API** (terminal 1, from the repo root)
```powershell
dotnet tool restore
dotnet dev-certs https --trust
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost;Database=InvoiceflowDev;Trusted_Connection=True;TrustServerCertificate=True" --project api/Invoiceflow.Api
dotnet ef database update --project api/Invoiceflow.Api
dotnet run --project api/Invoiceflow.Api
```

**Web** (terminal 2)
```powershell
cd web
npm ci
npm run dev
```

| URL | What it is |
| --- | --- |
| http://localhost:5173 | React app (calls the API through the Vite proxy) |
| https://localhost:7171/swagger | API docs (development only) |
| https://localhost:7171/api/health/db | Database health: Healthy / Unhealthy |

## Build log

| Day | Date | What I built |
| --- | --- | --- |
| 01 | Oct 19 | Repository structure |
| 02 | Oct 20 | ASP.NET Core API with health check and Swagger |
| 03 | Oct 21 | React app with design tokens and live API status |
| 04 | Oct 22 | EF Core + SQL Server with database health check |
| 05 | Oct 23 | Client entity, first migration and seeded demo clients |
| 06 | Oct 24 | Clients CRUD endpoints with DTOs and validation |
| 07 | Oct 25 | Week 1 review: setup guide, HTTPS by default, dependency audit |
| 08 | Oct 26 | ASP.NET Core Identity user store and migration |
## Author

Adeniyi Richard Adedokun, full stack developer & cybersecurity analyst
[Portfolio](https://adeniyi-richard.netlify.app) · [GitHub](https://github.com/TheNiyiRichard)