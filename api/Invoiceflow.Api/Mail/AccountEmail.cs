using Invoiceflow.Api.Models;

namespace Invoiceflow.Api.Mail;

// Emails about your own account, from Invoiceflow itself. Password reset joins tomorrow.
public static class AccountEmail
{
    public const int LinkHours = 24;

    public static EmailMessage Verify(ApplicationUser user, string link)
    {
        var first = user.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";

        var html = InvoiceEmail.Page("Invoiceflow", "Verify your email",
            InvoiceEmail.P($"Hi {InvoiceEmail.H(first)},") +
            InvoiceEmail.P("Confirm this is your email address, and you can start sending invoices."),
            link, "Verify email",
            $"This link works for {LinkHours} hours. If you didn’t create an Invoiceflow account, ignore this email and nothing will happen.");

        var text = $"""
            Hi {first},

            Confirm this is your email address, and you can start sending invoices:
            {link}

            This link works for {LinkHours} hours. If you didn't create an Invoiceflow account, ignore this email and nothing will happen.
            """;

        return new EmailMessage(user.Email!, InvoiceEmail.OneLine(user.FullName), "Verify your email for Invoiceflow", html, text);
    }
}