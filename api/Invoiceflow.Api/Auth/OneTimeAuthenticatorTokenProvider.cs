using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Invoiceflow.Api.Data;
using Invoiceflow.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Invoiceflow.Api.Auth;

// Checks authenticator-app codes. Identity's built-in check accepts a code for about 2.5 minutes and lets it be
// used again in that time. This one allows ±30 seconds, and each code works once (RFC 6238, section 5.2).
public class OneTimeAuthenticatorTokenProvider(AppDbContext db, TimeProvider clock) : AuthenticatorTokenProvider<ApplicationUser>
{
    public const string Provider = "OneTimeAuthenticator";
    const int StepSeconds = 30;
    const int Drift = 1; // one step either side, for a phone clock that's slightly off

    public override async Task<bool> ValidateAsync(string purpose, string token, UserManager<ApplicationUser> manager, ApplicationUser user)
    {
        var key = await manager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key) || token.Length != 6 || !token.All(char.IsAsciiDigit)) return false;

        var secret = FromBase32(key);
        var now = clock.GetUtcNow().ToUnixTimeSeconds() / StepSeconds;
        for (var step = now - Drift; step <= now + Drift; step++)
        {
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Code(secret, step)), Encoding.ASCII.GetBytes(token))) continue;

            // Claimed in one statement: only a step later than the last one used. Two requests racing
            // with the same code can't both get in, and a code someone watched you type is already spent.
            var claimed = await db.Users
                .Where(u => u.Id == user.Id && (u.LastTotpStep == null || u.LastTotpStep < step))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastTotpStep, (long?)step));

            // Identity saves the whole user row afterwards (lockout count, 2FA on/off),
            // so the loaded copy must match, or it would write the old value back
            if (claimed == 1) user.LastTotpStep = step;
            return claimed == 1;
        }
        return false;
    }

    // RFC 6238: HMAC-SHA1 of the 30-second step number, cut down to 6 digits
    static string Code(byte[] secret, long step)
    {
        var counter = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, step);
        var hash = HMACSHA1.HashData(secret, counter);
        var offset = hash[^1] & 0x0F;
        var number = (BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7FFFFFFF) % 1_000_000;
        return number.ToString("D6");
    }

    static byte[] FromBase32(string key)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in key.ToUpperInvariant())
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits < 8) continue;
            bits -= 8;
            bytes.Add((byte)(buffer >> bits));
            buffer &= (1 << bits) - 1;
        }
        return [.. bytes];
    }
}