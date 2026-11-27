using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Invoiceflow.Api.Dtos;

namespace Invoiceflow.Api.Tests;

// Codes worked out the way an authenticator app does it (RFC 6238): HMAC-SHA1 of the current 30-second step
static class Totp
{
    public static string Code(string key, DateTimeOffset? at = null)
    {
        var counter = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, (at ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds() / 30);
        var hash = HMACSHA1.HashData(FromBase32(key), counter);
        var offset = hash[^1] & 0x0F;
        var number = (BinaryPrimitives.ReadInt32BigEndian(hash.AsSpan(offset, 4)) & 0x7FFFFFFF) % 1_000_000;
        return number.ToString("D6");
    }
    // The code the app shows next. The current one is spent once it's been used.
    public static string Next(string key) => Code(key, DateTimeOffset.UtcNow.AddSeconds(30));

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

[Collection("api")]
public class TwoFactorTests(ApiFactory factory)
{
    static async Task<MeDto> Me(HttpClient browser) => (await browser.GetFromJsonAsync<MeDto>("/api/auth/me"))!;

    static Task<HttpResponseMessage> Setup(HttpClient browser, string password = ApiFactory.Password) =>
        browser.PostAsJsonAsync("/api/auth/2fa/setup", new { password });

    static Task<HttpResponseMessage> Enable(HttpClient browser, string code) =>
        browser.PostAsJsonAsync("/api/auth/2fa/enable", new { code });

    static Task<HttpResponseMessage> Disable(HttpClient browser, string code) =>
        browser.PostAsJsonAsync("/api/auth/2fa/disable", new { code });

    // A code the app showed 10 minutes ago: genuine, but long expired
    static string Stale(string key) => Totp.Code(key, DateTimeOffset.UtcNow.AddMinutes(-10));

    static async Task<string> KeyFrom(HttpResponseMessage setup)
    {
        Assert.Equal(HttpStatusCode.OK, setup.StatusCode);
        return (await setup.Content.ReadFromJsonAsync<TwoFactorSetupDto>())!.SharedKey.Replace(" ", "");
    }

    static async Task<string> TurnOn(HttpClient browser)
    {
        var key = await KeyFrom(await Setup(browser));
        Assert.Equal(HttpStatusCode.NoContent, (await Enable(browser, Totp.Code(key))).StatusCode);
        return key;
    }

    static async Task AssertFieldError(HttpResponseMessage res, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Contains(errors.EnumerateObject(), e => e.Name.Equals(field, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Turning_on_needs_the_password_then_a_code_from_the_app()
    {
        var (browser, email) = await factory.NewAccount("t");

        // A stolen session alone can't attach a new phone
        await AssertFieldError(await Setup(browser, "not my password at all"), "password");

        var res = await Setup(browser);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(res.Headers.CacheControl?.NoStore); // the secret is never cached
        var setup = (await res.Content.ReadFromJsonAsync<TwoFactorSetupDto>())!;
        var key = setup.SharedKey.Replace(" ", "");
        Assert.StartsWith($"otpauth://totp/Invoiceflow:{Uri.EscapeDataString(email)}?", setup.AuthenticatorUri);
        Assert.Contains($"secret={key}&issuer=Invoiceflow", setup.AuthenticatorUri);

        // Nothing is on until the app proves it has the secret
        await AssertFieldError(await Enable(browser, Stale(key)), "code");
        Assert.False((await Me(browser)).TwoFactorEnabled);

        Assert.Equal(HttpStatusCode.NoContent, (await Enable(browser, Totp.Code(key))).StatusCode);
        Assert.True((await Me(browser)).TwoFactorEnabled); // and this browser is still signed in

        // Shown once: there's no way to read the secret again while 2FA is on
        Assert.Equal(HttpStatusCode.Conflict, (await Setup(browser)).StatusCode);
    }

    [Fact]
    public async Task Turning_it_on_signs_out_every_other_device()
    {
        var (browser, email) = await factory.NewAccount("o");
        var laptop = factory.NewBrowser();
        Assert.Equal(HttpStatusCode.NoContent,
            (await laptop.PostAsJsonAsync("/api/auth/login", new { email, password = ApiFactory.Password })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.GetAsync("/api/auth/me")).StatusCode);

        await TurnOn(browser);

        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await laptop.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Turning_off_needs_a_current_code_and_retires_the_old_secret()
    {
        var (browser, _) = await factory.NewAccount("d");
        var key = await TurnOn(browser);

        // A stolen session alone can't switch it off
        await AssertFieldError(await Disable(browser, Stale(key)), "code");
        Assert.True((await Me(browser)).TwoFactorEnabled);

        // The code that turned it on is spent, so use the next one
        Assert.Equal(HttpStatusCode.NoContent, (await Disable(browser, Totp.Next(key))).StatusCode);
        Assert.False((await Me(browser)).TwoFactorEnabled);

        // Setting up again gives a new secret, so codes from the old one are useless
        var fresh = await KeyFrom(await Setup(browser));
        Assert.NotEqual(key, fresh);
        await AssertFieldError(await Enable(browser, Totp.Code(key)), "code");
    }

    const string NewPassword = "violet tram orchard snow";

    static Task<HttpResponseMessage> Login(HttpClient browser, string email, string password = ApiFactory.Password) =>
        browser.PostAsJsonAsync("/api/auth/login", new { email, password });

    static Task<HttpResponseMessage> LoginCode(HttpClient browser, string code, bool trustDevice = false) =>
        browser.PostAsJsonAsync("/api/auth/login/2fa", new { code, trustDevice });

    // The password was right, so it asks for the code, but nobody is signed in yet
    static async Task AssertCodeAsked(HttpResponseMessage res, HttpClient browser)
    {
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True((await res.Content.ReadFromJsonAsync<LoginStepDto>())!.TwoFactorRequired);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Signing_in_needs_the_password_then_a_code_that_works_once()
    {
        var (owner, email) = await factory.NewAccount("s");
        var key = await TurnOn(owner);

        var phone = factory.NewBrowser();
        await AssertCodeAsked(await Login(phone, email), phone);
        await AssertFieldError(await LoginCode(phone, Stale(key)), "code");

        var code = Totp.Next(key);
        Assert.Equal(HttpStatusCode.NoContent, (await LoginCode(phone, code)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/api/auth/me")).StatusCode);

        // Someone who watched the code being typed, and knows the password, still can't use it
        var thief = factory.NewBrowser();
        await AssertCodeAsked(await Login(thief, email), thief);
        await AssertFieldError(await LoginCode(thief, code), "code");
        Assert.Equal(HttpStatusCode.Unauthorized, (await thief.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task A_code_without_the_password_step_gets_nowhere()
    {
        var (owner, _) = await factory.NewAccount("n");
        var key = await TurnOn(owner);

        var stranger = factory.NewBrowser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginCode(stranger, Totp.Next(key))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task A_trusted_device_skips_the_code_until_the_password_changes()
    {
        var (owner, email) = await factory.NewAccount("u");
        var key = await TurnOn(owner);

        var laptop = factory.NewBrowser();
        await AssertCodeAsked(await Login(laptop, email), laptop);
        Assert.Equal(HttpStatusCode.NoContent, (await LoginCode(laptop, Totp.Next(key), trustDevice: true)).StatusCode);

        // Signing out keeps the trust: on this browser the password is enough. It's never skipped.
        Assert.Equal(HttpStatusCode.NoContent, (await laptop.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(laptop, email, "not the password at all")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Login(laptop, email)).StatusCode);

        // Any other browser still needs a code
        var other = factory.NewBrowser();
        await AssertCodeAsked(await Login(other, email), other);

        // A password reset forgets every trusted device
        Assert.Equal(HttpStatusCode.NoContent, (await factory.NewBrowser().PostAsJsonAsync("/api/auth/forgot-password", new { email })).StatusCode);
        var (userId, resetCode) = ApiFactory.ResetLink(Assert.Single(factory.Mailbox.To(email), m => m.Subject.Contains("password")));
        Assert.Equal(HttpStatusCode.NoContent,
            (await factory.NewBrowser().PostAsJsonAsync("/api/auth/reset-password", new { userId, code = resetCode, password = NewPassword })).StatusCode);
        await AssertCodeAsked(await Login(laptop, email, NewPassword), laptop);
    }

    [Fact]
    public async Task Five_wrong_codes_lock_sign_in()
    {
        var (owner, email) = await factory.NewAccount("l");
        var key = await TurnOn(owner);

        var browser = factory.NewBrowser();
        await AssertCodeAsked(await Login(browser, email), browser);
        for (var i = 0; i < 4; i++) await AssertFieldError(await LoginCode(browser, Stale(key)), "code");
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginCode(browser, Stale(key))).StatusCode); // the 5th locks it

        // Locked: the right password gets the usual vague answer for the next 15 minutes
        Assert.Equal(HttpStatusCode.Unauthorized, (await Login(factory.NewBrowser(), email)).StatusCode);
    }
}