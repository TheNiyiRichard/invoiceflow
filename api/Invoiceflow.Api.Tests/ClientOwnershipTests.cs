using System.Net;
using System.Net.Http.Json;
using Invoiceflow.Api.Dtos;

namespace Invoiceflow.Api.Tests;

// Proves one user can't read, change or delete another user's clients (IDOR)
[Collection("api")]
public class ClientOwnershipTests(ApiFactory factory)
{
    [Fact]
    public async Task Signed_out_requests_get_401()
    {
        var res = await factory.NewBrowser().GetAsync("/api/clients");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Users_cannot_see_each_others_clients()
    {
        var a = await factory.SignedInUser("a");
        var b = await factory.SignedInUser("b");
        var client = await a.AddClient("Client of A");

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
        var a = await factory.SignedInUser("a");
        var b = await factory.SignedInUser("b");
        var original = await a.AddClient("Client of A");

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