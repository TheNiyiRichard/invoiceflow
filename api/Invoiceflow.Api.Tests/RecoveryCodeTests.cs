using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Invoiceflow.Api.Data;
using Invoiceflow.Api.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Invoiceflow.Api.Tests;

// Backup codes for a lost phone: shown once, saved only as hashes, each one works once
[Collection("api")]
public class RecoveryCodeTests(ApiFactory factory)
{
    static async Task<MeDto> Me(HttpClient browser) => (await browser.GetFromJsonAsync<MeDto>("/api/auth/me"))!;

    static async Task<List<string>> CodesFrom(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(res.Headers.CacheControl?.NoStore); // shown once, never cached
        return [.. (await res.Content.ReadFromJsonAsync<RecoveryCodesDto>())!.Codes];
    }

    // 2FA on, and the ten codes shown at the end of setup
    static async Task<(string Key, List<string> Codes)> TurnOn(HttpClient browser)
    {
        var setup = await browser.PostAsJsonAsync("/api/auth/2fa/setup", new { password = ApiFactory.Password });
        var key = (await setup.Content.ReadFromJsonAsync<TwoFactorSetupDto>())!.SharedKey.Replace(" ", "");
        return (key, await CodesFrom(await browser.PostAsJsonAsync("/api/auth/2fa/enable", new { code = Totp.Code(key) })));
    }

    // A new browser where the password step is done and the code step is next
    async Task<HttpClient> PasswordDone(string email)
    {
        var browser = factory.NewBrowser();
        var res = await browser.PostAsJsonAsync("/api/auth/login", new { email, password = ApiFactory.Password });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True((await res.Content.ReadFromJsonAsync<LoginStepDto>())!.TwoFactorRequired);
        return browser;
    }

    static Task<HttpResponseMessage> UseCode(HttpClient browser, string code) =>
        browser.PostAsJsonAsync("/api/auth/login/recovery", new { code });

    static async Task AssertRefused(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var errors = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Contains(errors.EnumerateObject(), e => e.Name.Equals("code", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Turning_on_gives_ten_codes_and_saves_only_hashes()
    {
        var (browser, email) = await factory.NewAccount("rc");
        var (_, codes) = await TurnOn(browser);

        Assert.Equal(10, codes.Count);
        Assert.Equal(10, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^[A-Z2-9]{5}-[A-Z2-9]{5}$", c));
        Assert.Equal(10, (await Me(browser)).RecoveryCodesLeft);

        // A leaked database doesn't hand them out
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        var hashes = await db.RecoveryCodes.Where(c => c.UserId == userId).Select(c => c.Hash).ToListAsync();
        Assert.Equal(10, hashes.Count);
        foreach (var code in codes)
            Assert.DoesNotContain(hashes, h => h.Contains(code.Replace("-", "")));
        // And none in Identity's own store, which would keep them as plain text
        Assert.False(await db.UserTokens.AnyAsync(t => t.UserId == userId && t.Name == "RecoveryCodes"));
    }

    [Fact]
    public async Task A_recovery_code_signs_in_once()
    {
        var (owner, email) = await factory.NewAccount("rs");
        var (_, codes) = await TurnOn(owner);

        // Typed in lower case, with a space instead of the dash: still accepted
        var lostPhone = await PasswordDone(email);
        Assert.Equal(HttpStatusCode.NoContent, (await UseCode(lostPhone, codes[0].ToLowerInvariant().Replace("-", " "))).StatusCode);
        Assert.Equal(9, (await Me(lostPhone)).RecoveryCodesLeft);

        // Someone who saw that code, and knows the password, still can't use it
        var thief = await PasswordDone(email);
        await AssertRefused(await UseCode(thief, codes[0]));
        Assert.Equal(HttpStatusCode.Unauthorized, (await thief.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task A_recovery_code_without_the_password_step_gets_nowhere()
    {
        var (owner, _) = await factory.NewAccount("rn");
        var (_, codes) = await TurnOn(owner);

        var stranger = factory.NewBrowser();
        Assert.Equal(HttpStatusCode.Unauthorized, (await UseCode(stranger, codes[0])).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Five_wrong_recovery_codes_lock_sign_in()
    {
        var (owner, email) = await factory.NewAccount("rk");
        await TurnOn(owner);

        var browser = await PasswordDone(email);
        for (var i = 0; i < 4; i++) await AssertRefused(await UseCode(browser, "AAAAA-AAAAA"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await UseCode(browser, "AAAAA-AAAAA")).StatusCode); // the 5th locks it

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.NewBrowser().PostAsJsonAsync("/api/auth/login", new { email, password = ApiFactory.Password })).StatusCode);
    }

    [Fact]
    public async Task A_lost_phone_can_turn_2fa_off_with_a_recovery_code()
    {
        var (owner, email) = await factory.NewAccount("rl");
        var (_, old) = await TurnOn(owner);

        var browser = await PasswordDone(email);
        Assert.Equal(HttpStatusCode.NoContent, (await UseCode(browser, old[0])).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await browser.PostAsJsonAsync("/api/auth/2fa/disable", new { code = old[1] })).StatusCode);
        var me = await Me(browser);
        Assert.False(me.TwoFactorEnabled);
        Assert.Equal(0, me.RecoveryCodesLeft);

        // Setting up the new phone gives a new set, and the old codes are gone for good
        var (_, fresh) = await TurnOn(browser);
        Assert.Empty(fresh.Intersect(old));
        await AssertRefused(await UseCode(await PasswordDone(email), old[2]));
    }

    [Fact]
    public async Task A_new_set_needs_the_app_and_replaces_the_old_one()
    {
        var (owner, email) = await factory.NewAccount("rr");
        var (key, old) = await TurnOn(owner);

        await AssertRefused(await owner.PostAsJsonAsync("/api/auth/2fa/recovery-codes", new { code = Totp.Code(key, DateTimeOffset.UtcNow.AddMinutes(-10)) }));
        var fresh = await CodesFrom(await owner.PostAsJsonAsync("/api/auth/2fa/recovery-codes", new { code = Totp.Next(key) }));
        Assert.Equal(10, (await Me(owner)).RecoveryCodesLeft);
        Assert.Empty(fresh.Intersect(old));

        await AssertRefused(await UseCode(await PasswordDone(email), old[0])); // every old code stopped working
        Assert.Equal(HttpStatusCode.NoContent, (await UseCode(await PasswordDone(email), fresh[0])).StatusCode);
    }
}