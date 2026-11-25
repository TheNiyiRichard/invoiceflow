using System.Text;
using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Mail;
using Invoiceflow.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;

namespace Invoiceflow.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    IMailer mailer, IConfiguration config, ILogger<AuthController> logger) : ControllerBase
{
    // One message for every bad link, so nothing can be learned from it
    const string BadLink = "This link is invalid or has expired.";

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterInput input)
    {
        var email = input.Email.Trim();
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = input.FullName.Trim(),
            BusinessName = string.IsNullOrWhiteSpace(input.BusinessName) ? null : input.BusinessName.Trim(),
        };
        var result = await users.CreateAsync(user, input.Password);
        if (result.Succeeded)
        {
            // If the email fails, the account still exists: they can sign in and resend it from the banner
            await SendVerification(user);
            return StatusCode(StatusCodes.Status201Created);
        }

        var emailRejected = false;
        foreach (var e in result.Errors)
        {
            if (e.Code.StartsWith("Password")) ModelState.AddModelError(nameof(input.Password), e.Description);
            else emailRejected = true;
        }
        if (emailRejected) ModelState.AddModelError(nameof(input.Email), "This email can't be used to register.");
        return ValidationProblem(ModelState);
    }

    // POST /api/auth/verify-email: the page the email links to posts here.
    // Never a GET: email scanners open links, and opening a link must not change anything.
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailInput input)
    {
        var user = await users.FindByIdAsync(input.UserId);
        if (user is null) return InvalidLink();
        if (user.EmailConfirmed) return NoContent(); // an old link clicked again: already done
        if (Decode(input.Code) is not { } token) return InvalidLink();

        // Identity checks it's signed, unexpired and made for this exact account
        var result = await users.ConfirmEmailAsync(user, token);
        return result.Succeeded ? NoContent() : InvalidLink();
    }

    // POST /api/auth/resend-verification: a fresh link, from the banner. Shares the per-user email limit.
    [Authorize]
    [EnableRateLimiting("send")]
    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (user.EmailConfirmed) return NoContent();
        return await SendVerification(user)
            ? NoContent()
            : Problem(statusCode: StatusCodes.Status502BadGateway, title: "Couldn't send the email. Try again in a moment.");
    }

    // POST /api/auth/forgot-password: always the same empty 204, whether or not the account exists,
    // so this can't be used to find out who has an Invoiceflow account
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordInput input)
    {
        var user = await users.FindByEmailAsync(input.Email.Trim());
        if (user is not null) await SendReset(user);
        return NoContent();
    }

    // POST /api/auth/reset-password: the page the reset email links to posts here
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordInput input)
    {
        var user = await users.FindByIdAsync(input.UserId);
        if (user is null || Decode(input.Code) is not { } token) return InvalidLink();

        // Identity checks the link first (signed, under an hour old, for this account), then the new password's rules.
        // A new password also changes the account's security stamp. That makes this link (and any other
        // outstanding one) stop working, and signs out every browser that was signed in.
        var result = await users.ResetPasswordAsync(user, token, input.Password);
        if (!result.Succeeded)
        {
            var passwordErrors = result.Errors.Where(e => e.Code.StartsWith("Password")).ToList();
            if (passwordErrors.Count == 0) return InvalidLink();
            foreach (var e in passwordErrors) ModelState.AddModelError(nameof(input.Password), e.Description);
            return ValidationProblem(ModelState);
        }

        await signIn.SignOutAsync(); // this browser too: everyone signs in again with the new password
        logger.LogInformation("Password reset for user {UserId}", user.Id);
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginInput input)
    {
        var result = await signIn.PasswordSignInAsync(input.Email.Trim(), input.Password, isPersistent: false, lockoutOnFailure: true);
        if (result.Succeeded) return NoContent();

        // Same message for wrong password, unknown email and locked account,
        // so nobody can learn which accounts exist or are locked
        return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid email or password.");
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<MeDto>> Me()
    {
        var user = await users.GetUserAsync(User);
        return user is null ? Unauthorized() : new MeDto(user.Email!, user.FullName, user.BusinessName, user.EmailConfirmed);
    }

    // Links carry a signed token made for this account. They're never logged.
    async Task<bool> SendVerification(ApplicationUser user)
    {
        try
        {
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            await mailer.SendAsync(AccountEmail.Verify(user, AccountLink("verify-email", user, token)));
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Verification email for user {UserId} wasn't sent", user.Id);
            return false;
        }
    }

    async Task SendReset(ApplicationUser user)
    {
        try
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            await mailer.SendAsync(AccountEmail.Reset(user, AccountLink("reset-password", user, token)));
        }
        catch (Exception ex)
        {
            // Logged, never shown: the reply has to be the same either way
            logger.LogError(ex, "Password reset email for user {UserId} wasn't sent", user.Id);
        }
    }

    string AccountLink(string page, ApplicationUser user, string token) =>
        $"{config["App:WebUrl"]}/{page}?user={Uri.EscapeDataString(user.Id)}&code={WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token))}";

    static string? Decode(string code)
    {
        try { return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code)); }
        catch (FormatException) { return null; }
    }

    IActionResult InvalidLink() => Problem(statusCode: StatusCodes.Status400BadRequest, title: BadLink);
}