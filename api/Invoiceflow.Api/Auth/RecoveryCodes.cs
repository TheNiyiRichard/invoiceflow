using System.Security.Cryptography;
using Invoiceflow.Api.Data;
using Invoiceflow.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Invoiceflow.Api.Auth;

// Ten single-use backup codes for a lost phone. Identity has its own, but saves them as plain text by default.
// These are hashed with the same PBKDF2 hasher as passwords, so a leaked database doesn't hand them out.
public class RecoveryCodes(AppDbContext db, IPasswordHasher<ApplicationUser> hasher, TimeProvider clock)
{
    public const int Count = 10;
    const int Length = 10;
    // No 0/O or 1/I, so they're easy to read off paper. 32 characters = 50 random bits per code.
    const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    // A fresh set, shown once. Every old code stops working, used or not.
    public async Task<List<string>> ReplaceAsync(ApplicationUser user)
    {
        await DeleteAllAsync(user.Id);
        var now = clock.GetUtcNow().UtcDateTime;
        var codes = Enumerable.Range(0, Count).Select(_ => RandomNumberGenerator.GetString(Alphabet, Length)).ToList();
        db.RecoveryCodes.AddRange(codes.Select(c => new RecoveryCode { UserId = user.Id, Hash = hasher.HashPassword(user, c), CreatedAt = now }));
        await db.SaveChangesAsync();
        return codes.Select(c => $"{c[..5]}-{c[5..]}").ToList(); // ABCDE-FGHJK
    }

    public Task<int> RemainingAsync(string userId) =>
        db.RecoveryCodes.CountAsync(c => c.UserId == userId && c.UsedAt == null);

    // True once per code. Accepted in any case, with or without the dash.
    public async Task<bool> RedeemAsync(ApplicationUser user, string typed)
    {
        var code = string.Concat(typed.Where(char.IsAsciiLetterOrDigit)).ToUpperInvariant();
        if (code.Length != Length) return false;

        var unused = await db.RecoveryCodes.AsNoTracking()
            .Where(c => c.UserId == user.Id && c.UsedAt == null)
            .Select(c => new { c.Id, c.Hash })
            .ToListAsync();
        foreach (var c in unused)
        {
            if (hasher.VerifyHashedPassword(user, c.Hash, code) == PasswordVerificationResult.Failed) continue;

            // Claimed in one statement: if two requests race with the same code, only one gets in
            var now = clock.GetUtcNow().UtcDateTime;
            var claimed = await db.RecoveryCodes
                .Where(r => r.Id == c.Id && r.UsedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.UsedAt, (DateTime?)now));
            return claimed == 1;
        }
        return false;
    }

    public Task DeleteAllAsync(string userId) =>
        db.RecoveryCodes.Where(c => c.UserId == userId).ExecuteDeleteAsync();
}