using Flow.Launcher.Plugin.GitHubCli;
using Xunit;

namespace Flow.Launcher.Plugin.GitHubCli.Tests;

public sealed class QueryParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t")]
    [InlineData("home")]
    public void Parse_HomeInputs_ReturnHome(string? input)
    {
        var result = QueryParser.Parse(input);

        Assert.Equal(QueryKind.Home, result.Kind);
    }

    [Theory]
    [InlineData("repo")]
    [InlineData("repos")]
    [InlineData("R")]
    public void Parse_RepositoryCommandWithoutText_ReturnsMyRepositories(string input)
    {
        var result = QueryParser.Parse(input);

        Assert.Equal(QueryKind.MyRepositories, result.Kind);
        Assert.Equal(string.Empty, result.SearchText);
    }

    [Theory]
    [InlineData("repo flow launcher", "flow launcher")]
    [InlineData("r  C#; calc", "C#; calc")]
    [InlineData("中文仓库", "中文仓库")]
    public void Parse_RepositorySearch_PreservesSearchText(
        string input,
        string expectedSearchText)
    {
        var result = QueryParser.Parse(input);

        Assert.Equal(QueryKind.RepositorySearch, result.Kind);
        Assert.Equal(expectedSearchText, result.SearchText);
    }

    [Fact]
    public void Parse_GlobalPullRequestSearch_PreservesQualifiers()
    {
        var result = QueryParser.Parse("p author:@me is:open");

        Assert.Equal(QueryKind.PullRequests, result.Kind);
        Assert.Null(result.Repository);
        Assert.Equal("author:@me is:open", result.SearchText);
    }

    [Fact]
    public void Parse_RepositoryPullRequests_SeparatesRepositoryAndSearchText()
    {
        var result = QueryParser.Parse("  pr   Owner.Name/repo_name   fix bug  ");

        Assert.Equal(QueryKind.PullRequests, result.Kind);
        Assert.Equal("Owner.Name/repo_name", result.Repository);
        Assert.Equal("fix bug", result.SearchText);
    }

    [Theory]
    [InlineData("owner/repo", (int)QueryKind.DirectRepository, null)]
    [InlineData("owner/repo#123", (int)QueryKind.DirectPullRequest, 123)]
    [InlineData("pr owner/repo#456", (int)QueryKind.DirectPullRequest, 456)]
    public void Parse_DirectTargets_ReturnExpectedKind(
        string input,
        int expectedKind,
        int? expectedNumber)
    {
        var result = QueryParser.Parse(input);

        Assert.Equal((QueryKind)expectedKind, result.Kind);
        Assert.Equal("owner/repo", result.Repository);
        Assert.Equal(expectedNumber, result.PullRequestNumber);
    }

    [Theory]
    [InlineData("owner/repo#0")]
    [InlineData("owner/repo#abc")]
    [InlineData("pr owner/repo#0")]
    public void Parse_InvalidDirectPullRequest_ReturnsInvalid(string input)
    {
        var result = QueryParser.Parse(input);

        Assert.Equal(QueryKind.Invalid, result.Kind);
        Assert.NotNull(result.ErrorMessage);
    }

    [Theory]
    [InlineData("me", "")]
    [InlineData("M bug label:urgent", "bug label:urgent")]
    public void Parse_MyWork_ReturnsOptionalSearchText(
        string input,
        string expectedSearchText)
    {
        var result = QueryParser.Parse(input);

        Assert.Equal(QueryKind.MyWork, result.Kind);
        Assert.Equal(expectedSearchText, result.SearchText);
    }

    [Theory]
    [InlineData("org", "")]
    [InlineData("o open", "open")]
    [InlineData("ORG   platform team", "platform team")]
    public void Parse_Organizations_ReturnsLocalFilter(
        string input,
        string expectedSearchText)
    {
        var result = QueryParser.Parse(input);

        Assert.Equal(QueryKind.Organizations, result.Kind);
        Assert.Equal(expectedSearchText, result.SearchText);
    }

    [Theory]
    [InlineData("trend", (int)TrendPeriod.Weekly, null)]
    [InlineData("trend daily", (int)TrendPeriod.Daily, null)]
    [InlineData("trend 1d C#", (int)TrendPeriod.Daily, "C#")]
    [InlineData("t 7 rust", (int)TrendPeriod.Weekly, "rust")]
    [InlineData("trend monthly Jupyter Notebook", (int)TrendPeriod.Monthly, "Jupyter Notebook")]
    [InlineData("trend rust", (int)TrendPeriod.Weekly, "rust")]
    [InlineData("trend language:TypeScript", (int)TrendPeriod.Weekly, "TypeScript")]
    public void Parse_Trend_ReturnsPeriodAndOptionalLanguage(
        string input,
        int expectedPeriod,
        string? expectedLanguage)
    {
        var result = QueryParser.Parse(input);

        Assert.Equal(QueryKind.Trend, result.Kind);
        Assert.Equal((TrendPeriod)expectedPeriod, result.TrendPeriod);
        Assert.Equal(expectedLanguage, result.Language);
    }

    [Theory]
    [InlineData("trend 0")]
    [InlineData("trend 2d")]
    [InlineData("trend 31")]
    [InlineData("trend weekly language:")]
    public void Parse_InvalidTrend_ReturnsInvalid(string input)
    {
        var result = QueryParser.Parse(input);

        Assert.Equal(QueryKind.Invalid, result.Kind);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Parse_HomeWithArguments_ReturnsInvalid()
    {
        var result = QueryParser.Parse("home extra");

        Assert.Equal(QueryKind.Invalid, result.Kind);
    }
}
