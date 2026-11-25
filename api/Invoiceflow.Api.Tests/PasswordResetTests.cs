using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Invoiceflow.Api.Auth;
using Invoiceflow.Api.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Invoiceflow.Api.Tests;

[Collection("api")]
public class PasswordResetTests(ApiFactory factory)
{
    const string NewPassword = "violet tram orchard snow";

    // Signed out, like someone on the "Forgot password?" page
    Task<HttpResponseMessage> Forgot(string email) =>
        factory.NewBrowser().PostAsJsonAsync("/api/auth/forgot-password", new { email });

    // Signed out, like someone clicking the link in their inbox
    Task<HttpResponseMessage> Reset(string userId, string code, string password = NewPassword) =>
        factory.NewBrowser().PostAsJsonAsync("/api/auth/reset-password", new { userId, code, password });

    Task<HttpResponseMessage> SignIn(string email, string password) =>
        factory.NewBrowser().PostAsJsonAsync("/api/auth/login", new { email, password });

    EmailMessage ResetMail(string email) => Assert.Single(factory.Mailbox.To(email), m => m.Subject.Contains("password"));

    static async Task AssertBadLink(HttpResponseMessage res)
    {
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("This link is invalid or has expired.", body.GetProperty("title").GetString());
    }

    // A signed-in account that has asked for a reset, and the link from that email
    async Task<(HttpClient Browser, string Email, string UserId, string Code)> ResetRequested(string tag)
    {
        var (browser, email) = await factory.NewAccount(tag);
        Assert.Equal(HttpStatusCode.NoContent, (await Forgot(email)).StatusCode);
        var (userId, code) = ApiFactory.ResetLink(ResetMail(email));
        return (browser, email, userId, code);
    }

    [Fact]
    public async Task The_reply_is_the_same_whether_or_not_the_account_exists()
    {
        var (_, email) = await factory.NewAccount("f");
        var nobody = $"nobody-{Guid.NewGuid():N}@example.com";

        var known = await Forgot(email);
        var unknown = await Forgot(nobody);

        Assert.Equal(HttpStatusCode.NoContent, known.StatusCode);
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
        ResetMail(email);                       // the real account got its link
        Assert.Empty(factory.Mailbox.To(nobody)); // nobody else got anything
    }

    [Fact]
    public async Task A_reset_link_works_once_and_signs_out_every_old_session()
    {
        var (browser, email, userId, code) = await ResetRequested("p");
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await Reset(userId, code)).StatusCode);

        // The browser that was signed in before the reset is signed out now
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/auth/me")).StatusCode);
        // Only the new password works
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn(email, ApiFactory.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SignIn(email, NewPassword)).StatusCode);
        // The same link can't be used twice
        await AssertBadLink(await Reset(userId, code, "another quiet harbour morning"));
    }

    [Fact]
    public async Task A_weak_new_password_is_refused_and_the_link_still_works()
    {
        var (_, _, userId, code) = await ResetRequested("q");

        var weak = await Reset(userId, code, "short");
        Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);
        var errors = (await weak.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");
        Assert.Contains(errors.EnumerateObject(), e => e.Name.Equals("password", StringComparison.OrdinalIgnoreCase));

        // Nothing was used up: the same link still works with a good password
        Assert.Equal(HttpStatusCode.NoContent, (await Reset(userId, code)).StatusCode);
    }

    [Theory]
    [InlineData("one-character-changed")]
    [InlineData("a-verification-code")]
    [InlineData("not-a-code")]
    [InlineData("unknown-account")]
    public async Task Bad_links_change_nothing(string attack)
    {
        var (browser, email) = await factory.NewAccount("z", verified: false);
        Assert.Equal(HttpStatusCode.NoContent, (await Forgot(email)).StatusCode);
        var (userId, code) = ApiFactory.ResetLink(ResetMail(email));
        var (_, verificationCode) = ApiFactory.VerificationLink(Assert.Single(factory.Mailbox.To(email), m => m.Subject.StartsWith("Verify")));

        var res = attack switch
        {
            "one-character-changed" => await Reset(userId, code[..20] + (code[20] == 'A' ? 'B' : 'A') + code[21..]),
            "a-verification-code" => await Reset(userId, verificationCode), // genuine, same account, made for something else
            "not-a-code" => await Reset(userId, "not*a*code"),
            _ => await Reset(Guid.NewGuid().ToString(), code),
        };

        await AssertBadLink(res);
        // Nothing changed: the session from before is still signed in, so the password wasn't touched
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public void Reset_links_last_an_hour_and_verification_links_keep_24()
    {
        var identity = factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;
        Assert.Equal(PasswordResetTokenOptions.Provider, identity.Tokens.PasswordResetTokenProvider);
        Assert.Equal(TimeSpan.FromHours(1), factory.Services.GetRequiredService<IOptions<PasswordResetTokenOptions>>().Value.TokenLifespan);
        Assert.Equal(TimeSpan.FromHours(24), factory.Services.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan);
    }
}