using Invoiceflow.Api.Models;
using Microsoft.AspNetCore.Identity;

namespace Invoiceflow.Api.Auth;

public class CommonPasswordValidator : IPasswordValidator<ApplicationUser>
{
    static readonly string[] Banned = ["password", "qwerty", "123456", "letmein", "welcome", "admin", "invoiceflow"];

    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        var p = (password ?? "").ToLowerInvariant();
        var emailName = (user.Email ?? "").Split('@')[0].ToLowerInvariant();
        var weak = Banned.Any(w => p.Contains(w)) || (emailName.Length >= 3 && p.Contains(emailName));

        return Task.FromResult(weak
            ? IdentityResult.Failed(new IdentityError { Code = "PasswordTooCommon", Description = "Choose a less predictable password. Avoid common words and your email." })
            : IdentityResult.Success);
    }
}