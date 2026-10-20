using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Promuse.Contracts.Auth;

namespace Promuse.Api.Tests;

/// <summary>
/// 代理后面的真实地址 / Deployed, every request reaches the API through the reverse proxy, so every
/// one arrives from the proxy's address. ASPNETCORE_FORWARDEDHEADERS_ENABLED - set by the deploy, and
/// FORWARDEDHEADERS_ENABLED to the host - makes the API take the caller from X-Forwarded-For instead,
/// which is what lets the sign-in limit count each player rather than one bucket for everyone.
///
/// It trusts the header from anyone, so it is only safe while the proxy is the one way in: the proxy
/// replaces whatever X-Forwarded-For a client sends. Off - the default, and local development - the
/// header is not believed at all.
/// </summary>
[Collection(ApiCollection.Name)]
public class ForwardedHeadersTests(PromuseApiFactory factory)
{
    private const int Limit = 2;

    private HttpClient Api(bool behindProxy) => factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("RateLimit:AuthPermitPerMinute", Limit.ToString());
        if (behindProxy) builder.UseSetting("FORWARDEDHEADERS_ENABLED", "true");
    }).CreateClient();

    /// <summary>A sign-in the limiter counts; the account does not exist, so it is refused either way.</summary>
    private static async Task<HttpStatusCode> SignInFromAsync(HttpClient client, string address)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/v1/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("nobody_" + Guid.NewGuid().ToString("N")[..10], "not the password")),
        };
        request.Headers.Add("X-Forwarded-For", address);

        return (await client.SendAsync(request)).StatusCode;
    }

    [Fact]
    public async Task Behind_the_proxy_each_caller_has_a_limit_of_their_own()
    {
        HttpClient client = Api(behindProxy: true);

        for (int i = 0; i < Limit; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await SignInFromAsync(client, "203.0.113.10"));
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, await SignInFromAsync(client, "203.0.113.10"));

        // Someone else, through the same proxy, is not locked out by the first one.
        Assert.Equal(HttpStatusCode.Unauthorized, await SignInFromAsync(client, "198.51.100.20"));
    }

    [Fact]
    public async Task Without_it_a_forwarded_address_is_not_believed()
    {
        HttpClient client = Api(behindProxy: false);

        for (int i = 0; i < Limit; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, await SignInFromAsync(client, "203.0.113.10"));
        }

        // A new address in the header changes nothing: the limit is the connection's, not the header's.
        Assert.Equal(HttpStatusCode.TooManyRequests, await SignInFromAsync(client, "198.51.100.20"));
    }
}
