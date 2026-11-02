using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Invoiceflow.Api.Tests;

// Every endpoint must refuse signed-out requests, except the ones listed here.
// A new endpoint that's accidentally public fails this test the day it's written.
[Collection("api")]
public class EndpointAuthTests(ApiFactory factory)
{
    static readonly HashSet<string> Public =
    [
        "POST /api/auth/register",
        "POST /api/auth/login",
        "GET /api/health",
        "GET /api/health/db",
    ];

    [Fact]
    public async Task Every_endpoint_requires_sign_in_unless_allowlisted()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? new[] { "GET" })
                .Select(method => (method, path: "/" + e.RoutePattern.RawText!.TrimStart('/'))))
            .Distinct()
            .ToList();

        // Guard: if route discovery ever breaks and finds nothing, don't pass silently
        Assert.Contains(endpoints, e => e.path == "/api/clients");

        var browser = factory.NewBrowser(); // signed out
        var failures = new List<string>();

        foreach (var (method, path) in endpoints)
        {
            if (Public.Contains($"{method} {path}")) continue;

            // Fill route values like {id:guid} with a random id
            var url = Regex.Replace(path, @"\{[^}]+\}", Guid.NewGuid().ToString());
            var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (method is "POST" or "PUT" or "PATCH") request.Content = JsonContent.Create(new { });

            var res = await browser.SendAsync(request);
            if (res.StatusCode != HttpStatusCode.Unauthorized)
                failures.Add($"{method} {path} answered {(int)res.StatusCode} while signed out");
        }

        Assert.Empty(failures);
    }
}