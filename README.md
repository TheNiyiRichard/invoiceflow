# InvoiceFlow

Invoicing for freelancers, with security built in from day one.

Create invoices, send them with signed payment links, get paid by card, and see every action in an audit log.

> 🚧 Building in public: 8 weeks, one commit a day (Oct 19 – Dec 13, 2026).

## Stack

- **API:** ASP.NET Core (.NET 10), Entity Framework Core
- **Web:** React + Vite
- **Database:** SQL Server
- **Payments:** Stripe (test mode)
- **PDFs:** QuestPDF (Community licence)
- **Email:** SMTP via MailKit (Mailtrap sandbox in development)

## Security features (planned)

- [x] Secure cookie sessions with account lockout
- [x] Per-user data isolation (IDOR protection), covered by automated tests
- [ ] Two-factor sign-in (TOTP) with recovery codes
- [x] Signed, expiring payment links (HMAC-SHA256)
- [x] Verified Stripe webhooks
- [x] Email verification before an account can send invoices
- [ ] Tamper-evident audit log
- [ ] Role-based access (Owner, Accountant, Viewer)
- [ ] Encryption of sensitive fields at rest
- [ ] Security headers, CSP and rate limiting

## Project structure

- `api/`: ASP.NET Core Web API
- `api/Invoiceflow.Api.Tests/`: integration tests
- `web/`: React front end

## Run it locally

**You need:** .NET 10 SDK · Node.js LTS · SQL Server 2022 (Developer edition) · free Mailtrap and Stripe (test mode) accounts

**API** (terminal 1, from the repo root)
```powershell
dotnet tool restore
dotnet dev-certs https --trust
dotnet user-secrets set "ConnectionStrings:Default" "Server=localhost;Database=InvoiceflowDev;Trusted_Connection=True;TrustServerCertificate=True" --project api/Invoiceflow.Api
$bytes = New-Object byte[] 32; [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
dotnet user-secrets set "PaymentLinks:Key" ([Convert]::ToBase64String($bytes)) --project api/Invoiceflow.Api
dotnet user-secrets set "Smtp:Username" "your-mailtrap-username" --project api/Invoiceflow.Api
dotnet user-secrets set "Smtp:Password" "your-mailtrap-password" --project api/Invoiceflow.Api
dotnet user-secrets set "Stripe:SecretKey" "sk_test_..." --project api/Invoiceflow.Api
dotnet user-secrets set "Stripe:PublishableKey" "pk_test_..." --project api/Invoiceflow.Api
stripe listen --forward-to https://localhost:7171/api/webhooks/stripe --skip-verify   # separate terminal; prints whsec_…
dotnet user-secrets set "Stripe:WebhookSecret" "whsec_..." --project api/Invoiceflow.Api
dotnet ef database update --project api/Invoiceflow.Api
dotnet run --project api/Invoiceflow.Api --launch-profile https
```
Secrets live in user-secrets, never in the repo. Only Stripe **test** keys are accepted.

**Web** (terminal 2)
```powershell
cd web
npm ci
npm run dev
```

**Tests** (from the repo root, API stopped)
```powershell
dotnet test
```
Uses a separate `InvoiceflowTest` database that is deleted and rebuilt on every run. Email and Stripe are replaced with fakes, so tests never send mail or touch Stripe.

| URL | What it is |
| --- | --- |
| http://localhost:5173 | React app (calls the API through the Vite proxy) |
| https://localhost:7171/swagger | API docs (development only) |
| https://localhost:7171/api/health/db | Database health: Healthy / Unhealthy |
| https://localhost:7171/api/health/jobs | Background job health: Unhealthy if the overdue check hasn't run in 2 hours |

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
| 09 | Oct 27 | Register endpoint with strong password rules and unique emails |
| 10 | Oct 28 | Cookie login and logout (HttpOnly, Secure, SameSite=Strict) |
| 11 | Oct 29 | Account lockout and rate-limited sign-in |
| 12 | Oct 30 | Sign-in and register screens in React |
| 13 | Oct 31 | Per-user data and protected routes |
| 14 | Nov 1 | Week 2 review: automated tests proving users can't reach each other's clients |
| 15 | Nov 2 | Sign-in required by default; invoice and line item tables |
| 16 | Nov 3 | Invoice endpoints with status filter, paging and ownership checks |
| 17 | Nov 4 | Server-side totals with decimal maths and INV-0001 numbering |
| 18 | Nov 5 | App shell: dark sidebar, top bar and routing |
| 19 | Nov 6 | Invoices list with status tabs, pills and pagination |
| 20 | Nov 7 | Invoice editor: client picker, dates and line items |
| 21 | Nov 8 | Live invoice preview and draft autosave |
| 22 | Nov 9 | Manage clients in the app; delete draft invoices |
| 23 | Nov 10 | Background job that marks unpaid invoices overdue |
| 24 | Nov 11 | Dashboard stats endpoint: outstanding, paid this month, drafts, days to pay |
| 25 | Nov 12 | Cash-flow endpoint: 12 months of paid vs outstanding |
| 26 | Nov 13 | Dashboard: KPI band, cash-flow chart and recent invoices |
| 27 | Nov 14 | Invoice PDFs built on the server, tested against the API |
| 28 | Nov 15 | Week 4 review: health check for the overdue job, loading skeletons |
| 29 | Nov 16 | Signed, expiring payment links (HMAC-SHA256) |
| 30 | Nov 17 | Send invoices by email with the PDF and a signed payment link |
| 31 | Nov 18 | Public payment page: opens only with a signed link, marks invoices Viewed |
| 32 | Nov 19 | Card payments with Stripe (test mode): server-side amount, one payment per invoice |
| 33 | Nov 20 | Verified Stripe webhook: signature, replay and amount checks before marking Paid |
| 34 | Nov 21 | Payment receipts and invoice reminders (one per invoice per day) |
| 35 | Nov 22 | Week 5 review: webhook payments confirmed with Stripe, tampered-link tests on every pay route |
| 36 | Nov 23 | Email verification: signed 24-hour links; unverified accounts can't email anyone |
## Author

Adeniyi Richard Adedokun, full stack developer & cybersecurity analyst
[Portfolio](https://adeniyi-richard.netlify.app) · [GitHub](https://github.com/TheNiyiRichard)