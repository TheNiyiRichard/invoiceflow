using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Invoiceflow.Api.Payments;

public enum LinkStatus { Valid, Expired, Invalid }

// A payment link carries an invoice id and an expiry time, signed with a key only the server knows (HMAC-SHA256).
// Change a single character and the signature stops matching, so nobody can point a link at another
// invoice or push its expiry back. Nothing is stored: the signature is the proof.
public class PaymentLinks(byte[] key, string webUrl, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    const byte Version = 1;
    const int PayloadBytes = 1 + 16 + 8;      // version, invoice id, expiry
    const int TokenBytes = PayloadBytes + 32; // + the HMAC-SHA256 signature
    const int TokenLength = 76;               // 57 bytes in base64url, no padding

    // Also signs what the token is FOR, so a signature made for anything else can never pass as a payment link
    static readonly byte[] Purpose = Encoding.ASCII.GetBytes("invoiceflow/payment-link/");

    // A fresh link for the invoice, valid for 30 days
    public (string Url, DateTimeOffset ExpiresAt) CreateLink(Guid invoiceId)
    {
        // Whole seconds: the same precision the link stores
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(clock.GetUtcNow().Add(Lifetime).ToUnixTimeSeconds());
        return ($"{webUrl.TrimEnd('/')}/pay/{Create(invoiceId, expiresAt)}", expiresAt);
    }

    public string Create(Guid invoiceId, DateTimeOffset expiresAt)
    {
        var token = new byte[TokenBytes];
        token[0] = Version;
        invoiceId.TryWriteBytes(token.AsSpan(1, 16));
        BinaryPrimitives.WriteInt64BigEndian(token.AsSpan(17, 8), expiresAt.ToUnixTimeSeconds());
        Sign(token.AsSpan(0, PayloadBytes)).CopyTo(token, PayloadBytes);
        return Base64Url.EncodeToString(token);
    }

    public (LinkStatus Status, Guid InvoiceId) Check(string? token)
    {
        // Wrong length or not base64url: refused before any real work
        var bytes = new byte[TokenBytes];
        if (token is null || token.Length != TokenLength || !IsBase64Url(token)
            || !Base64Url.TryDecodeFromChars(token, bytes, out var written) || written != TokenBytes)
            return (LinkStatus.Invalid, Guid.Empty);

        // Compared in constant time, so response timing can't reveal how much of a forged signature was right
        var payload = bytes.AsSpan(0, PayloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(Sign(payload), bytes.AsSpan(PayloadBytes)) || payload[0] != Version)
            return (LinkStatus.Invalid, Guid.Empty);

        var invoiceId = new Guid(payload.Slice(1, 16));
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadInt64BigEndian(payload.Slice(17, 8)));

        // Saying "expired" is safe here: only a link this server signed gets this far
        return clock.GetUtcNow() < expiresAt ? (LinkStatus.Valid, invoiceId) : (LinkStatus.Expired, invoiceId);
    }

        // Only A–Z, a–z, 0–9, - and _ get as far as decoding. The decoder throws on anything else.
    static bool IsBase64Url(string s) =>
        s.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_');

    byte[] Sign(ReadOnlySpan<byte> payload)
    {
        var data = new byte[Purpose.Length + payload.Length];
        Purpose.CopyTo(data, 0);
        payload.CopyTo(data.AsSpan(Purpose.Length));
        return HMACSHA256.HashData(key, data);
    }

    // The key comes from user-secrets as base64. Missing or too short: the API refuses to start.
    public static byte[] ReadKey(string? base64)
    {
        try
        {
            var key = Convert.FromBase64String(base64 ?? "");
            if (key.Length >= 32) return key;
        }
        catch (FormatException) { }
        throw new InvalidOperationException("PaymentLinks:Key is missing or shorter than 32 bytes. Set it with dotnet user-secrets (see README).");
    }
}