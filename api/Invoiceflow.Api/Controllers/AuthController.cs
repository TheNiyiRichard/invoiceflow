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
        if (user is null) return Problem(statusCode: StatusCodes.Status400BadRequest, title: BadLink);
        if (user.EmailConfirmed) return NoContent(); // an old link clicked again: already done

        string token;
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(input.Code));
        }
        catch (FormatException)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: BadLink);
        }

        // Identity checks it's signed, unexpired and made for this exact account
        var result = await users.ConfirmEmailAsync(user, token);
        return result.Succeeded ? NoContent() : Problem(statusCode: StatusCodes.Status400BadRequest, title: BadLink);
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

    // The link carries a signed token made for this account. It's never logged.
    async Task<bool> SendVerification(ApplicationUser user)
    {
        try
        {
            var token = await users.GenerateEmailConfirmationTokenAsync(user);
            var code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token)); // URL-safe, no + / =
            var link = $"{config["App:WebUrl"]}/verify-email?user={Uri.EscapeDataString(user.Id)}&code={code}";
            await mailer.SendAsync(AccountEmail.Verify(user, link));
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Verification email for user {UserId} wasn't sent", user.Id);
            return false;
        }
    }
}