using System;
using NUnit.Framework;
using Promuse.Contracts.Auth;
using Promuse.Net;
using UnityEngine;

// 连哪个服务器 / Which server a build talks to, what it keeps apart on the device, and which builds
// are refused before they start.
public class ServerTargetTests
{
    private static ServerTarget Target(ServerEnvironment environment, string staging = "", string production = "")
    {
        ServerTarget target = ScriptableObject.CreateInstance<ServerTarget>();
        target.environment = environment;
        target.stagingUrl = staging;
        target.productionUrl = production;
        return target;
    }

    [Test]
    public void Local_IsTheDevelopmentApi_AndKeepsTheNamesADeviceAlreadyHas()
    {
        ServerTarget local = Target(ServerEnvironment.Local);

        Assert.AreEqual("http://127.0.0.1:5199", local.BaseUrl);
        Assert.AreEqual("", local.StorageScope);

        PromuseConfig config = local.CreateConfig();
        Assert.AreEqual("Promuse.Auth.RefreshToken", config.Scoped("Promuse.Auth.RefreshToken"));
        Assert.AreEqual("/data", config.DataDirectory("/data"));
    }

    [Test]
    public void Staging_KeepsItsOwnKeysAndFolder()
    {
        PromuseConfig config = Target(ServerEnvironment.Staging, staging: "https://x-staging.duckdns.org/").CreateConfig();

        Assert.AreEqual("https://x-staging.duckdns.org", config.baseUrl, "no trailing slash: paths start with one");
        Assert.AreEqual("Promuse.Auth.RefreshToken.staging", config.Scoped("Promuse.Auth.RefreshToken"));
        Assert.AreEqual(System.IO.Path.Combine("/data", "staging"), config.DataDirectory("/data"));
    }

    [Test]
    public void ABuildThatCannotWork_IsRefused_WithWhy()
    {
        StringAssert.Contains("empty", Target(ServerEnvironment.Staging).Problem(developmentBuild: true));
        StringAssert.Contains("HTTPS", Target(ServerEnvironment.Production, production: "http://x.duckdns.org").Problem(true));
        StringAssert.Contains("release build", Target(ServerEnvironment.Local).Problem(developmentBuild: false));
    }

    [Test]
    public void ABuildThatCanWork_IsNot()
    {
        Assert.IsNull(Target(ServerEnvironment.Local).Problem(developmentBuild: true));
        Assert.IsNull(Target(ServerEnvironment.Staging, staging: "https://x-staging.duckdns.org").Problem(true));
        Assert.IsNull(Target(ServerEnvironment.Production, production: "https://x.duckdns.org").Problem(false));
    }

    [Test]
    public void ASession_IsKeptPerServer()
    {
        // A scope of the test's own, so neither a real staging session nor Local's is touched.
        string scope = "test-" + Guid.NewGuid().ToString("N");
        var tokens = new TokenPair("access", TokenPair.Bearer, 900, "refresh-" + scope, 2592000);

        try
        {
            new TokenStore(scope).Adopt(new AuthSession(Guid.NewGuid(), false, tokens, DateTimeOffset.UtcNow));

            Assert.AreEqual("refresh-" + scope, new TokenStore(scope).RefreshToken, "the same server finds its session again");
            Assert.AreNotEqual("refresh-" + scope, new TokenStore().RefreshToken, "Local never sees it");
        }
        finally
        {
            new TokenStore(scope).Clear();
        }

        Assert.IsFalse(new TokenStore(scope).HasSession);
    }
}
