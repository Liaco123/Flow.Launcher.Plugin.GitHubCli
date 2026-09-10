using Flow.Launcher.Plugin.GitHubCli;
using Xunit;

namespace Flow.Launcher.Plugin.GitHubCli.Tests;

public sealed class GitHubCliRunnerTests
{
    [Theory]
    [InlineData("GH_TOKEN=plain-secret-value", "plain-secret-value")]
    [InlineData("GITHUB_ENTERPRISE_TOKEN=enterprise-secret", "enterprise-secret")]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload", "eyJhbGciOiJIUzI1NiJ9.payload")]
    [InlineData("clone https://user:password123@example.com/repo", "user:password123")]
    [InlineData("clone https://token-value@example.com/repo", "token-value")]
    [InlineData("server returned ghp_1234567890abcdefghij", "ghp_1234567890abcdefghij")]
    [InlineData("server returned github_pat_1234567890abcdefghij", "github_pat_1234567890abcdefghij")]
    public void SanitizeError_RedactsCredentialPatterns(string stderr, string secret)
    {
        var result = GitHubCliRunner.SanitizeError(stderr);

        Assert.DoesNotContain(secret, result, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", result, StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeError_RemovesControlCharactersAndBoundsOutput()
    {
        var result = GitHubCliRunner.SanitizeError($"\u0001{new string('x', 600)}");

        Assert.DoesNotContain("\u0001", result, StringComparison.Ordinal);
        Assert.Equal(500, result.Length);
    }
}
