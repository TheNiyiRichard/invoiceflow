using System.Net;
using System.Net.Http.Json;
using Invoiceflow.Api.Dtos;

namespace Invoiceflow.Api.Tests;

// Proves one user can't read, change or delete another user's clients (IDOR)
public class ClientOwnershipTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    const string Password = "copper lantern harbour drift";

    // Registers a brand-new user and returns a browser signed in as them.
    // Uses 2 of the 10 auth requests a minute the rate limiter allows. Keep the total under 10.
    async Task<HttpClient> SignedInUser(string tag)
    {
        var browser = factory.NewBrowser();
        var email = $"{tag}-{Guid.NewGuid():N}@example.com";

        var register = await browser.PostAsJsonAsync("/api/auth/register", new { fullName = $"Tester {tag}", email, password = Password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var login = await browser.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        return browser;
    }

    static async Task<ClientDto> AddClient(HttpClient browser, string name)
    {
        var res = await browser.PostAsJsonAsync("/api/clients", new { name, email = "client@example.com" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<ClientDto>())!;
    }

    [Fact]
    public async Task Signed_out_requests_get_401()
    {
        var res = await factory.NewBrowser().GetAsync("/api/clients");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Users_cannot_see_each_others_clients()
    {
        var a = await SignedInUser("a");
        var b = await SignedInUser("b");
        var client = await AddClient(a, "Client of A");

        // Control: the owner can see it, so the 404s below aren't just a broken endpoint
        var aList = await a.GetFromJsonAsync<List<ClientDto>>("/api/clients");
        Assert.Contains(aList!, c => c.Id == client.Id);

        var bList = await b.GetFromJsonAsync<List<ClientDto>>("/api/clients");
        Assert.DoesNotContain(bList!, c => c.Id == client.Id);

        // 404, not 403: B can't even confirm the client exists
        var direct = await b.GetAsync($"/api/clients/{client.Id}");
        Assert.Equal(HttpStatusCode.NotFound, direct.StatusCode);
    }

    [Fact]
    public async Task Users_cannot_edit_or_delete_each_others_clients()
    {
        var a = await SignedInUser("a");
        var b = await SignedInUser("b");
        var original = await AddClient(a, "Client of A");

        var put = await b.PutAsJsonAsync($"/api/clients/{original.Id}", new { name = "Hacked by B", email = "hacked@example.com" });
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);

        var delete = await b.DeleteAsync($"/api/clients/{original.Id}");
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        // A's client is untouched
        var after = await a.GetFromJsonAsync<ClientDto>($"/api/clients/{original.Id}");
        Assert.Equal("Client of A", after!.Name);
        Assert.Equal(original.Email, after.Email);
    }
}