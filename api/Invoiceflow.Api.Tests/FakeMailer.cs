using System.Collections.Concurrent;
using Invoiceflow.Api.Mail;

namespace Invoiceflow.Api.Tests;

// Stands in for SMTP: messages land here instead. Anything to an @fail.test address fails, like a mail server that's down.
public class FakeMailer : IMailer
{
    public ConcurrentQueue<EmailMessage> Sent { get; } = new();

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (message.To.EndsWith("@fail.test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Mail server unavailable (test)");
        Sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public List<EmailMessage> To(string address) => Sent.Where(m => m.To == address).ToList();
}