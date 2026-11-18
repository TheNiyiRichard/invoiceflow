namespace Invoiceflow.Api.Mail;

public record EmailAttachment(string FileName, string ContentType, byte[] Content);

public record EmailMessage(string To, string ToName, string Subject, string Html, string Text)
{
    public string? FromName { get; init; }
    public string? ReplyTo { get; init; }
    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];
}

// Anything that sends email. SMTP in the app, a fake mailbox in tests. Days 36–37 reuse it.
public interface IMailer
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}