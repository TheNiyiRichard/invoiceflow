using System.Globalization;
using System.Net;
using Invoiceflow.Api.Models;
using Invoiceflow.Api.Pdf;

namespace Invoiceflow.Api.Mail;

// The email a client receives. Every typed value (client name, business name) goes through H(),
// so a client named "<a href=…>" shows up as text in someone's inbox, never as a link.
public static class InvoiceEmail
{
    static readonly CultureInfo Canada = CultureInfo.GetCultureInfo("en-CA");
    static string LongDate(DateOnly d) => d.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

    public static EmailMessage Build(Invoice invoice, InvoicePdf.Sender sender, string payUrl, DateTimeOffset linkExpires, byte[] pdf)
    {
        var number = InvoiceMath.FormatNumber(invoice.Number);
        var total = invoice.Total.ToString("C", Canada);
        var due = LongDate(invoice.DueDate);
        var expires = LongDate(DateOnly.FromDateTime(linkExpires.UtcDateTime));
        var from = OneLine(sender.Name);
        var client = invoice.Client;

        var html = $"""
            <!doctype html>
            <html><body style="margin:0;padding:24px;background:#EEEBE4;font-family:Helvetica,Arial,sans-serif;color:#151A17">
              <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="max-width:520px;margin:0 auto;background:#FFFEFB;border-radius:12px">
                <tr><td style="padding:32px">
                  <p style="margin:0 0 4px;font-size:13px;color:#6E726C">{H(from)}</p>
                  <h1 style="margin:0 0 20px;font-size:24px;font-weight:500">Invoice {number}</h1>
                  <p style="margin:0 0 16px;font-size:15px;line-height:1.5">Hi {H(client.Name)},</p>
                  <p style="margin:0 0 24px;font-size:15px;line-height:1.5">{H(from)} has sent you an invoice for <strong>{total}</strong>, due {due}.</p>
                  <a href="{H(payUrl)}" style="display:inline-block;padding:12px 20px;border-radius:8px;background:#151A17;color:#FFFEFB;font-size:15px;text-decoration:none">View and pay invoice</a>
                  <p style="margin:24px 0 0;font-size:13px;line-height:1.5;color:#6E726C">The invoice is attached as a PDF. This link works until {expires}. Reply to this email to reach {H(from)}.</p>
                </td></tr>
              </table>
              <p style="max-width:520px;margin:16px auto 0;font-size:12px;color:#6E726C">Sent with Invoiceflow</p>
            </body></html>
            """;

        var text = $"""
            Hi {client.Name},

            {from} has sent you invoice {number} for {total}, due {due}.

            View and pay: {payUrl}
            (This link works until {expires}.)

            The invoice is attached as a PDF. Reply to this email to reach {from}.
            """;

        return new EmailMessage(client.Email, OneLine(client.Name), $"Invoice {number} from {from}", html, text)
        {
            FromName = $"{from} via Invoiceflow",
            ReplyTo = string.IsNullOrWhiteSpace(sender.Email) ? null : sender.Email,
            Attachments = [new EmailAttachment($"{number}.pdf", "application/pdf", pdf)],
        };
    }

    static string H(string s) => WebUtility.HtmlEncode(s);

    // Names also go into email headers (subject, display names), where a line break could start a new header
    static string OneLine(string s) => string.Concat(s.Select(c => char.IsControl(c) ? ' ' : c)).Trim();
}