using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Invoiceflow.Api.Dtos;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Invoiceflow.Api.Tests;

// Walks every route with an {id} and checks user B gets 404 for user A's record.
// A new kind of resource fails this test until it's added to "targets" below.
[Collection("api")]
public class IdRouteOwnershipTests(ApiFactory factory)
{
    [Fact]
    public async Task Every_id_route_hides_other_users_records()
    {
        var a = await factory.SignedInUser("a");
        var b = await factory.SignedInUser("b");
        var aClient = await a.AddClient("Owned by A");
        var aInvoice = await a.AddInvoice(aClient.Id);
        var bClient = await b.AddClient("Owned by B"); // so B's request bodies are otherwise valid

        // Route prefix → A's record, plus a body that would be valid if B owned it
        var targets = new Dictionary<string, (Guid Id, object Body)>
        {
            ["/api/clients/"] = (aClient.Id, new { name = "Hacked by B", email = "hacked@example.com" }),
            ["/api/invoices/"] = (aInvoice.Id, TestApi.InvoiceBody(bClient.Id, "Hacked by B")),
        };

        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.Parameters.Any(p => p.Name == "id"))
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? new[] { "GET" })
                .Select(method => (method, path: "/" + e.RoutePattern.RawText!.TrimStart('/'))))
            .Distinct()
            .ToList();

        // Guard: don't pass silently if route discovery breaks
        Assert.Contains(routes, r => r.path.StartsWith("/api/invoices/"));

        var failures = new List<string>();
        foreach (var (method, path) in routes)
        {
            var prefix = path[..path.IndexOf('{')];
            if (!targets.TryGetValue(prefix, out var target))
            {
                failures.Add($"{method} {path} isn't covered: add '{prefix}' to targets");
                continue;
            }

            var url = Regex.Replace(path, @"\{id[^}]*\}", target.Id.ToString());
            var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (method is "POST" or "PUT" or "PATCH") request.Content = JsonContent.Create(target.Body);

            var res = await b.SendAsync(request);
            if (res.StatusCode != HttpStatusCode.NotFound)
                failures.Add($"{method} {path} answered {(int)res.StatusCode} to another user");
        }
        Assert.Empty(failures);

        // Right status AND nothing changed: A's records are exactly as created
        var client = await a.GetFromJsonAsync<ClientDto>($"/api/clients/{aClient.Id}");
        Assert.Equal("Owned by A", client!.Name);

        var invoice = await a.GetFromJsonAsync<InvoiceDto>($"/api/invoices/{aInvoice.Id}");
        Assert.Equal(aClient.Id, invoice!.ClientId);
        Assert.Equal("Design work", invoice.LineItems.Single().Description);
    }
}