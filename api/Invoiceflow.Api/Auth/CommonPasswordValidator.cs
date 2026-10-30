using Invoiceflow.Api.Models;
using Microsoft.AspNetCore.Identity;

namespace Invoiceflow.Api.Auth;

public class CommonPasswordValidator : IPasswordValidator<ApplicationUser>
{
    static readonly string[] Banned = ["password", "qwerty", "123456", "letmein", "welcome", "admin", "invoiceflow"];

    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        var p = (password ?? "").ToLowerInvariant();
        var personal = (user.FullName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Append((user.Email ?? "").Split('@')[0])
            .Append((user.BusinessName ?? "").Replace(" ", ""))
            .Select(w => w.ToLowerInvariant())
            .Where(w => w.Length >= 3);

        var weak = Banned.Any(w => p.Contains(w)) || personal.Any(w => p.Contains(w));

        return Task.FromResult(weak
            ? IdentityResult.Failed(new IdentityError { Code = "PasswordTooCommon", Description = "Choose a less predictable password. Avoid common words, your name and your email." })
            : IdentityResult.Success);
    }
}