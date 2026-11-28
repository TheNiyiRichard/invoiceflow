using Invoiceflow.Api.Auth;
using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Invoiceflow.Api.Controllers;

// Two-factor sign-in with an authenticator app (TOTP). Sign-in is required by default (Day 15),
// so EndpointAuthTests already checks that every route here refuses signed-out requests.
// Rate-limited like sign-in, so nobody gets to try all million 6-digit codes.
[ApiController]
[Route("api/auth/2fa")]
[EnableRateLimiting("auth")]
public class TwoFactorController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    RecoveryCodes recoveryCodes, ILogger<TwoFactorController> logger) : ControllerBase
{
    const string Issuer = "Invoiceflow";
    const string BadCode = "That code didn't work. Use the newest code in your app, and check your phone sets its time automatically.";
    const string BadOffCode = "That code didn't work. Use the newest code in your app, or a recovery code you haven't used.";

    // POST /api/auth/2fa/setup: password first, then a brand-new secret for the app.
    // The password stops someone with a stolen session attaching their own phone and locking you out.
    [HttpPost("setup")]
    public async Task<ActionResult<TwoFactorSetupDto>> Setup(TwoFactorSetupInput input)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        // The secret is shown once. While 2FA is on, there's no way to read it again.
        if (user.TwoFactorEnabled) return Problem(statusCode: StatusCodes.Status409Conflict, title: "Two-factor sign-in is already on.");

        // Wrong passwords here count towards the 5-strike lockout, same as at sign-in
        var check = await signIn.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: true);
        if (!check.Succeeded)
        {
            ModelState.AddModelError(nameof(input.Password), check.IsLockedOut
                ? "Too many wrong passwords. Try again in 15 minutes."
                : "That password isn't right.");
            return ValidationProblem(ModelState);
        }

        user.LastTotpStep = null; // a new secret starts with no codes used (saved by the next line)
        await users.ResetAuthenticatorKeyAsync(user);
        await signIn.RefreshSignInAsync(user); // that renewed the account's security stamp: keep this browser signed in
        var key = (await users.GetAuthenticatorKeyAsync(user))!;

        Response.Headers.CacheControl = "no-store"; // a secret: never kept in a cache
        return new TwoFactorSetupDto(Format(key),
            $"otpauth://totp/{Issuer}:{Uri.EscapeDataString(user.Email!)}?secret={key}&issuer={Issuer}&digits=6&period=30");
    }

    // POST /api/auth/2fa/enable: the first code from the app proves it has the secret. Only then is 2FA on,
    // and the answer is 10 recovery codes, shown this once.
    [HttpPost("enable")]
    public async Task<ActionResult<RecoveryCodesDto>> Enable(TwoFactorCodeInput input)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (user.TwoFactorEnabled) return Problem(statusCode: StatusCodes.Status409Conflict, title: "Two-factor sign-in is already on.");
        if (await users.GetAuthenticatorKeyAsync(user) is null) return Problem(statusCode: StatusCodes.Status409Conflict, title: "Start the setup first.");
        if (!await ValidCode(user, input.Code)) return WrongCode(BadCode);

        await users.SetTwoFactorEnabledAsync(user, true);
        // New security stamp: every other device that was signed in is signed out within a minute. This one stays.
        await signIn.RefreshSignInAsync(user);
        logger.LogInformation("Two-factor sign-in turned on for user {UserId}", user.Id);
        return await FreshCodes(user);
    }

    // POST /api/auth/2fa/recovery-codes: a new set, for when they're used up or the list is lost.
    // Needs a current code from the app. Every old code stops working.
    [HttpPost("recovery-codes")]
    public async Task<ActionResult<RecoveryCodesDto>> NewRecoveryCodes(TwoFactorCodeInput input)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (!user.TwoFactorEnabled) return Problem(statusCode: StatusCodes.Status409Conflict, title: "Turn on two-factor sign-in first.");
        if (!await ValidCode(user, input.Code)) return WrongCode(BadCode);

        logger.LogInformation("New recovery codes made for user {UserId}", user.Id);
        return await FreshCodes(user);
    }

    // POST /api/auth/2fa/disable: needs a current code from the app, or a recovery code if the phone is lost,
    // so a stolen session alone can't switch it off
    [HttpPost("disable")]
    public async Task<IActionResult> Disable(RecoveryCodeInput input)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Unauthorized();
        if (!user.TwoFactorEnabled) return NoContent();

        var fromApp = input.Code.Where(c => c is not ' ' and not '-').All(char.IsAsciiDigit);
        var ok = fromApp ? await ValidCode(user, input.Code) : await recoveryCodes.RedeemAsync(user, input.Code);
        if (!ok) return WrongCode(BadOffCode);

        await users.SetTwoFactorEnabledAsync(user, false);
        await users.ResetAuthenticatorKeyAsync(user); // the old secret is retired: codes from that phone never work again
        await recoveryCodes.DeleteAllAsync(user.Id);  // and the backup codes go with it
        await signIn.RefreshSignInAsync(user);
        logger.LogInformation("Two-factor sign-in turned off for user {UserId}", user.Id);
        return NoContent();
    }

    async Task<RecoveryCodesDto> FreshCodes(ApplicationUser user)
    {
        Response.Headers.CacheControl = "no-store"; // shown once, never kept in a cache
        return new RecoveryCodesDto(await recoveryCodes.ReplaceAsync(user));
    }

    // The one-time check from Auth/OneTimeAuthenticatorTokenProvider.cs, same as at sign-in
    Task<bool> ValidCode(ApplicationUser user, string code) =>
        users.VerifyTwoFactorTokenAsync(user, users.Options.Tokens.AuthenticatorTokenProvider, string.Concat(code.Where(char.IsAsciiDigit)));

    ActionResult WrongCode(string message)
    {
        ModelState.AddModelError("Code", message);
        return ValidationProblem(ModelState);
    }

    // "ABCD EFGH IJKL …" is easier to type in by hand
    static string Format(string key) => string.Join(' ', key.Chunk(4).Select(c => new string(c)));
}