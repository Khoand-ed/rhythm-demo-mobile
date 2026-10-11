using Promuse.Contracts.Config;

namespace Promuse.Api.Tests;

/// <summary>
/// The one version comparison both sides use. Compiled into the game too, so a case pinned here is
/// a case the client decides the same way.
/// </summary>
public class ClientVersionTests
{
    [Theory]
    [InlineData("1.9.9", "2.0", true)]
    [InlineData("1.9", "1.10", true)]       // numbers, not strings: 9 < 10
    [InlineData("1.10", "1.9", false)]
    [InlineData("2.0.0", "2.0", false)]     // equal is not outdated
    [InlineData("2", "2.0.0", false)]
    [InlineData("1.0.0-beta", "1.0.1", true)] // a pre-release tag is ignored
    [InlineData("1.0", "0.0.0", false)]
    public void Older_than_the_minimum_is_outdated(string client, string minimum, bool outdated)
    {
        Assert.Equal(outdated, ClientVersion.IsOutdated(client, minimum));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("dev")]
    [InlineData("1.x")]
    public void A_client_version_that_does_not_parse_is_never_refused(string? client)
    {
        Assert.False(ClientVersion.IsOutdated(client, "9.9.9"));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("1.2.3", true)]
    [InlineData("1.2.3+build.7", true)]
    [InlineData("1.2.3.4", false)]
    [InlineData("-1.0", false)]
    [InlineData(" ", false)]
    public void What_counts_as_a_version(string text, bool valid)
    {
        Assert.Equal(valid, ClientVersion.IsValid(text));
    }
}
