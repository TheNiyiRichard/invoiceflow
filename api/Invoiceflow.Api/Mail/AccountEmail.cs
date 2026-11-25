using Invoiceflow.Api.Models;

namespace Invoiceflow.Api.Mail;

// Emails about your own account, from Invoiceflow itself
public static class AccountEmail
{
    public const int LinkHours = 24;

    public static EmailMessage Verify(ApplicationUser user, string link)
    {
        var first = FirstName(user);

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

    public static EmailMessage Reset(ApplicationUser user, string link)
    {
        var first = FirstName(user);

        var html = InvoiceEmail.Page("Invoiceflow", "Reset your password",
            InvoiceEmail.P($"Hi {InvoiceEmail.H(first)},") +
            InvoiceEmail.P("Someone asked to reset the password for your Invoiceflow account. If it was you, choose a new one below."),
            link, "Choose a new password",
            "This link works once, for 1 hour. If you didn’t ask for this, ignore this email: your password stays the same.");

        var text = $"""
            Hi {first},

            Someone asked to reset the password for your Invoiceflow account. If it was you, choose a new one here:
            {link}

            This link works once, for 1 hour. If you didn't ask for this, ignore this email: your password stays the same.
            """;

        return new EmailMessage(user.Email!, InvoiceEmail.OneLine(user.FullName), "Reset your Invoiceflow password", html, text);
    }

    static string FirstName(ApplicationUser user) =>
        user.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "there";
}