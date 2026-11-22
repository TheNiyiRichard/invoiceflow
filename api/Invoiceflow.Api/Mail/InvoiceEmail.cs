using System.Globalization;
using System.Net;
using Invoiceflow.Api.Models;
using Invoiceflow.Api.Pdf;

namespace Invoiceflow.Api.Mail;

// The emails a client receives: the invoice, a reminder and a receipt. Every typed value (client name, business name)
// goes through H(), so a client named "<a href=…>" shows up as text in someone's inbox, never as a link.
public static class InvoiceEmail
{
    static readonly CultureInfo Canada = CultureInfo.GetCultureInfo("en-CA");
    static string LongDate(DateOnly d) => d.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
    static string LongDate(DateTime utc) => LongDate(DateOnly.FromDateTime(utc));

    public static EmailMessage Build(Invoice invoice, InvoicePdf.Sender sender, string payUrl, DateTimeOffset linkExpires, byte[] pdf)
    {
        var (number, total, from) = Basics(invoice, sender);
        var name = invoice.Client.Name;
        var due = LongDate(invoice.DueDate);
        var expires = LongDate(linkExpires.UtcDateTime);

        var html = Page(from, $"Invoice {number}",
            P($"Hi {H(name)},") + P($"{H(from)} has sent you an invoice for <strong>{total}</strong>, due {due}."),
            payUrl, "View and pay invoice",
            $"The invoice is attached as a PDF. This link works until {expires}. Reply to this email to reach {H(from)}.");

        var text = $"""
            Hi {name},

            {from} has sent you invoice {number} for {total}, due {due}.

            View and pay: {payUrl}
            (This link works until {expires}.)

            The invoice is attached as a PDF. Reply to this email to reach {from}.
            """;

        return Message(invoice, sender, $"Invoice {number} from {from}", html, text, pdf);
    }

    // Same invoice, a freshly signed link: old links are never extended, a new one is signed
    public static EmailMessage Reminder(Invoice invoice, InvoicePdf.Sender sender, string payUrl, DateTimeOffset linkExpires, byte[] pdf)
    {
        var (number, total, from) = Basics(invoice, sender);
        var name = invoice.Client.Name;
        var due = LongDate(invoice.DueDate);
        var when = invoice.Status == InvoiceStatus.Overdue ? $"was due {due}" : $"is due {due}";
        var expires = LongDate(linkExpires.UtcDateTime);

        var html = Page(from, $"Reminder: invoice {number}",
            P($"Hi {H(name)},") +
            P($"A friendly reminder that invoice {number} from {H(from)} for <strong>{total}</strong> {when}. If you’ve already paid, thank you, and you can ignore this email."),
            payUrl, "View and pay invoice",
            $"The invoice is attached as a PDF. This new link works until {expires}. Reply to this email to reach {H(from)}.");

        var text = $"""
            Hi {name},

            A friendly reminder that invoice {number} from {from} for {total} {when}.
            If you've already paid, thank you, and you can ignore this email.

            View and pay: {payUrl}
            (This link works until {expires}.)

            The invoice is attached as a PDF. Reply to this email to reach {from}.
            """;

        return Message(invoice, sender, $"Reminder: invoice {number} from {from}", html, text, pdf);
    }

    // After the webhook marks it Paid. No link at all: there's nothing left to do.
    public static EmailMessage Receipt(Invoice invoice, InvoicePdf.Sender sender, byte[] pdf)
    {
        var (number, total, from) = Basics(invoice, sender);
        var name = invoice.Client.Name;
        var paidOn = LongDate(invoice.PaidAt ?? DateTime.UtcNow);

        var html = Page(from, $"Receipt for invoice {number}",
            P($"Hi {H(name)},") + P($"{H(from)} received your payment of <strong>{total}</strong> on {paidOn}. Thank you."),
            null, null,
            $"The paid invoice is attached as a PDF for your records. Reply to this email to reach {H(from)}.");

        var text = $"""
            Hi {name},

            {from} received your payment of {total} for invoice {number} on {paidOn}. Thank you.

            The paid invoice is attached as a PDF for your records. Reply to this email to reach {from}.
            """;

        return Message(invoice, sender, $"Receipt for invoice {number} from {from}", html, text, pdf);
    }

    static (string Number, string Total, string From) Basics(Invoice invoice, InvoicePdf.Sender sender) =>
        (InvoiceMath.FormatNumber(invoice.Number), invoice.Total.ToString("C", Canada), OneLine(sender.Name));

    static EmailMessage Message(Invoice invoice, InvoicePdf.Sender sender, string subject, string html, string text, byte[] pdf) =>
        new(invoice.Client.Email, OneLine(invoice.Client.Name), subject, html, text)
        {
            FromName = $"{OneLine(sender.Name)} via Invoiceflow",
            ReplyTo = string.IsNullOrWhiteSpace(sender.Email) ? null : sender.Email,
            Attachments = [new EmailAttachment($"{InvoiceMath.FormatNumber(invoice.Number)}.pdf", "application/pdf", pdf)],
        };

    static string P(string html) => $"""<p style="margin:0 0 16px;font-size:15px;line-height:1.5">{html}</p>""";

    // The shared layout. Everything passed in is already encoded.
    static string Page(string from, string heading, string body, string? buttonUrl, string? buttonText, string footnote)
    {
        var button = buttonUrl is null ? "" :
            $"""<a href="{H(buttonUrl)}" style="display:inline-block;margin-top:8px;padding:12px 20px;border-radius:8px;background:#151A17;color:#FFFEFB;font-size:15px;text-decoration:none">{buttonText}</a>""";

        return $"""
            <!doctype html>
            <html><body style="margin:0;padding:24px;background:#EEEBE4;font-family:Helvetica,Arial,sans-serif;color:#151A17">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;margin:0 auto;background:#FFFEFB;border-radius:12px">
                <tr><td style="padding:32px">
                  <p style="margin:0 0 4px;font-size:13px;color:#6E726C">{H(from)}</p>
                  <h1 style="margin:0 0 20px;font-size:24px;font-weight:500">{heading}</h1>
                  {body}
                  {button}
                  <p style="margin:24px 0 0;font-size:13px;line-height:1.5;color:#6E726C">{footnote}</p>
                </td></tr>
              </table>
              <p style="max-width:520px;margin:16px auto 0;font-size:12px;color:#6E726C">Sent with Invoiceflow</p>
            </body></html>
            """;
    }

    static string H(string s) => WebUtility.HtmlEncode(s);

    // Names also go into email headers (subject, display names), where a line break could start a new header
    static string OneLine(string s) => string.Concat(s.Select(c => char.IsControl(c) ? ' ' : c)).Trim();
}