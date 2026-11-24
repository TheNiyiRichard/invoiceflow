using Invoiceflow.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;

namespace Invoiceflow.Api.Auth;

// Anything that emails someone else needs an address we know is real,
// so nobody can sign up as anyone@anywhere and use Invoiceflow to send phishing.
public class VerifiedEmailRequirement : IAuthorizationRequirement
{
    public const string Policy = "VerifiedEmail";
}

// Read from the database on every request, not from the cookie, so it works the moment the email is verified
public class VerifiedEmailHandler(UserManager<ApplicationUser> users) : AuthorizationHandler<VerifiedEmailRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, VerifiedEmailRequirement requirement)
    {
        var user = await users.GetUserAsync(context.User);
        if (user?.EmailConfirmed == true) context.Succeed(requirement);
    }
}