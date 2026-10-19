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

## Build log

| Day | Date | What I built |
| --- | --- | --- |
| 01 | Oct 19 | Repository structure |

## Author

Adeniyi Richard Adedokun, full stack developer & cybersecurity analyst
[Portfolio](https://adeniyi-richard.netlify.app) · [GitHub](https://github.com/TheNiyiRichard)