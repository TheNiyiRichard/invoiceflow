using Invoiceflow.Api.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Invoiceflow.Api.Auth;

// Reset links get their own token provider, so they can expire after 1 hour
// while email verification links (the default provider) keep their 24 hours
public class PasswordResetTokenOptions : DataProtectionTokenProviderOptions
{
    public const string Provider = "PasswordReset";
    public const int LinkHours = 1;

    public PasswordResetTokenOptions()
    {
        Name = Provider;
        TokenLifespan = TimeSpan.FromHours(LinkHours);
    }
}

public class PasswordResetTokenProvider(IDataProtectionProvider protection, IOptions<PasswordResetTokenOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(protection, options, logger);