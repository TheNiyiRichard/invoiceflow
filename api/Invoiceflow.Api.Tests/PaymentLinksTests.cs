using System.Security.Cryptography;
using Invoiceflow.Api.Payments;

namespace Invoiceflow.Api.Tests;

// The signing on its own, with a clock the test controls
public class PaymentLinksTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);
    static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);
    static PaymentLinks Links(byte[]? key = null) => new(key ?? Key, "https://pay.test", new TestClock(Now));

    [Fact]
    public void A_link_names_its_invoice_until_it_expires()
    {
        var clock = new TestClock(Now);
        var links = new PaymentLinks(Key, "https://pay.test", clock);
        var id = Guid.NewGuid();
        var token = links.Create(id, Now.AddDays(30));

        Assert.Equal((LinkStatus.Valid, id), links.Check(token));

        clock.Now = Now.AddDays(30).AddSeconds(-1);
        Assert.Equal(LinkStatus.Valid, links.Check(token).Status);

        clock.Now = Now.AddDays(30);
        Assert.Equal((LinkStatus.Expired, id), links.Check(token));
    }

    [Fact]
    public void Changing_any_single_character_breaks_the_link()
    {
        var links = Links();
        var token = links.Create(Guid.NewGuid(), Now.AddDays(30));
        Assert.Equal(76, token.Length);

        // Every position: the version, the invoice id, the expiry and the signature itself
        for (var i = 0; i < token.Length; i++)
        {
            var chars = token.ToCharArray();
            chars[i] = chars[i] == 'A' ? 'B' : 'A';
            Assert.Equal(LinkStatus.Invalid, links.Check(new string(chars)).Status);
        }
    }

    [Fact]
    public void A_link_signed_with_another_key_is_rejected()
    {
        var forged = Links(RandomNumberGenerator.GetBytes(32)).Create(Guid.NewGuid(), Now.AddDays(30));
        Assert.Equal(LinkStatus.Invalid, Links().Check(forged).Status);
    }

    [Fact]
    public void Junk_is_rejected_without_throwing()
    {
        var links = Links();
        foreach (var junk in new[] { null, "", "not-a-token", new string('!', 76), new string('A', 76) })
            Assert.Equal(LinkStatus.Invalid, links.Check(junk).Status);
    }

    [Fact]
    public void A_missing_or_short_key_stops_the_app_starting()
    {
        Assert.Throws<InvalidOperationException>(() => PaymentLinks.ReadKey(null));
        Assert.Throws<InvalidOperationException>(() => PaymentLinks.ReadKey("not base64!"));
        Assert.Throws<InvalidOperationException>(() => PaymentLinks.ReadKey(Convert.ToBase64String(new byte[16])));
    }
}