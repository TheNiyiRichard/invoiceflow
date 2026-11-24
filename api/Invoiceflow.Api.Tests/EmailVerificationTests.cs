using System.Net;
using System.Net.Http.Json;
using Invoiceflow.Api.Dtos;
using Invoiceflow.Api.Models;

namespace Invoiceflow.Api.Tests;

[Collection("api")]
public class EmailVerificationTests(ApiFactory factory)
{
    static async Task<bool> Verified(HttpClient browser) =>
        (await browser.GetFromJsonAsync<MeDto>("/api/auth/me"))!.EmailConfirmed;

    // Signed out, like someone clicking the link in their inbox
    Task<HttpResponseMessage> Verify(string userId, string code) =>
        factory.NewBrowser().PostAsJsonAsync("/api/auth/verify-email", new { userId, code });

    [Fact]
    public async Task Signing_up_emails_a_link_that_verifies_the_account()
    {
        var (browser, email) = await factory.NewAccount("v", verified: false);
        Assert.False(await Verified(browser));

        var mail = Assert.Single(factory.Mailbox.To(email));
        Assert.Contains("Verify", mail.Subject);
        var (userId, code) = ApiFactory.VerificationLink(mail);

        Assert.Equal(HttpStatusCode.NoContent, (await Verify(userId, code)).StatusCode);
        Assert.True(await Verified(browser));

        // Clicking the same link again is fine: it's already done
        Assert.Equal(HttpStatusCode.NoContent, (await Verify(userId, code)).StatusCode);
    }

    [Theory]
    [InlineData("one-character-changed")]
    [InlineData("another-accounts-code")]
    [InlineData("not-a-code")]
    [InlineData("unknown-account")]
    public async Task Bad_links_are_refused(string attack)
    {
        var (browser, email) = await factory.NewAccount("x", verified: false);
        var (userId, code) = ApiFactory.VerificationLink(Assert.Single(factory.Mailbox.To(email)));

        var res = attack switch
        {
            "one-character-changed" => await Verify(userId, code[..20] + (code[20] == 'A' ? 'B' : 'A') + code[21..]),
            "another-accounts-code" => await Verify(userId, await OtherAccountsCode()),
            "not-a-code" => await Verify(userId, "not*a*code"),
            _ => await Verify(Guid.NewGuid().ToString(), code),
        };

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.False(await Verified(browser));
    }

    // A genuine code, made for a different account
    async Task<string> OtherAccountsCode()
    {
        var (_, other) = await factory.NewAccount("y", verified: false);
        return ApiFactory.VerificationLink(Assert.Single(factory.Mailbox.To(other))).Code;
    }

    [Fact]
    public async Task Unverified_accounts_can_draft_but_not_email_anyone()
    {
        var (u, email) = await factory.NewAccount("w", verified: false);
        var clientEmail = $"{Guid.NewGuid():N}@example.com";
        var client = await u.AddClient("Test client", clientEmail);
        var draft = await u.AddInvoice(client.Id); // drafting works

        Assert.Equal(HttpStatusCode.Forbidden, (await u.PostAsync($"/api/invoices/{draft.Id}/send", null)).StatusCode);
        Assert.Equal("Draft", await u.StatusOf(draft.Id));

        await factory.SetStatus(draft.Id, InvoiceStatus.Sent);
        Assert.Equal(HttpStatusCode.Forbidden, (await u.PostAsync($"/api/invoices/{draft.Id}/remind", null)).StatusCode);
        Assert.Empty(factory.Mailbox.To(clientEmail));

        // Once verified, the same request goes through: verification was the only thing in the way
        var (userId, code) = ApiFactory.VerificationLink(Assert.Single(factory.Mailbox.To(email)));
        Assert.Equal(HttpStatusCode.NoContent, (await Verify(userId, code)).StatusCode);
        Assert.True((await u.PostAsync($"/api/invoices/{draft.Id}/remind", null)).IsSuccessStatusCode);
        Assert.Single(factory.Mailbox.To(clientEmail));
    }

    [Fact]
    public async Task Resending_only_works_until_verified()
    {
        var (u, email) = await factory.NewAccount("r", verified: false);
        Assert.Equal(HttpStatusCode.NoContent, (await u.PostAsync("/api/auth/resend-verification", null)).StatusCode);
        var mails = factory.Mailbox.To(email);
        Assert.Equal(2, mails.Count);

        var (userId, code) = ApiFactory.VerificationLink(mails[^1]); // the newest link works
        Assert.Equal(HttpStatusCode.NoContent, (await Verify(userId, code)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await u.PostAsync("/api/auth/resend-verification", null)).StatusCode);
        Assert.Equal(2, factory.Mailbox.To(email).Count); // nothing more is sent once verified
    }
}