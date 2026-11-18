using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Invoiceflow.Api.Mail;

public class SmtpSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 2525;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "invoices@invoiceflow.test";
    public string FromName { get; set; } = "Invoiceflow";
}

public class SmtpMailer(IOptions<SmtpSettings> options) : IMailer
{
    public async Task SendAsync(EmailMessage email, CancellationToken ct = default)
    {
        var s = options.Value;
        if (string.IsNullOrWhiteSpace(s.Host) || string.IsNullOrWhiteSpace(s.Username) || string.IsNullOrWhiteSpace(s.Password))
            throw new InvalidOperationException("Smtp is not configured. Set Smtp:Username and Smtp:Password with dotnet user-secrets (see README).");

        var message = new MimeMessage();
        // Always our own address. Sending "from" the user's domain would be spoofing, and mail servers reject it (SPF/DMARC).
        message.From.Add(new MailboxAddress(email.FromName ?? s.FromName, s.FromAddress));
        message.To.Add(new MailboxAddress(email.ToName, email.To));
        if (email.ReplyTo is not null) message.ReplyTo.Add(new MailboxAddress(email.FromName ?? s.FromName, email.ReplyTo));
        message.Subject = email.Subject;

        var body = new BodyBuilder { HtmlBody = email.Html, TextBody = email.Text };
        foreach (var a in email.Attachments) body.Attachments.Add(a.FileName, a.Content, ContentType.Parse(a.ContentType));
        message.Body = body.ToMessageBody();

        using var smtp = new SmtpClient();
        // StartTls is required, not "if available": the password never crosses the network unencrypted
        await smtp.ConnectAsync(s.Host, s.Port, SecureSocketOptions.StartTls, ct);
        await smtp.AuthenticateAsync(s.Username, s.Password, ct);
        await smtp.SendAsync(message, ct);
        await smtp.DisconnectAsync(true, ct);
    }
}