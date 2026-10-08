using System.Reflection;
using System.Text.RegularExpressions;
using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.GitHubCli;
using Xunit;

namespace Flow.Launcher.Plugin.GitHubCli.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("en", "My repositories", "My organizations")]
    [InlineData("zh-cn", "我的仓库", "我的组织")]
    [InlineData("zh-tw", "我的儲存庫", "我的組織")]
    public void HomeMenu_UsesFlowTranslations(string language, string repositories, string organizations)
    {
        var (factory, proxy) = CreateFactory();
        proxy.Language = language;

        var results = factory.CreateHomeResults();

        Assert.Equal(repositories, results[0].Title);
        Assert.Equal(organizations, results[1].Title);
        Assert.Equal("gh repo ", results[0].AutoCompleteText);
        Assert.Equal("gh org ", results[1].AutoCompleteText);
    }

    [Fact]
    public void LanguageChange_UpdatesExistingContextMenuAndCachedResultLabels()
    {
        var (factory, proxy) = CreateFactory();
        var query = new ParsedQuery(QueryKind.Organizations);
        var data = new GitHubQueryResult([new GitHubItem(
            GitHubItemKind.Organization, "Acme", "Acme", "https://github.com/Acme")], IsFromCache: true);
        var oldResult = Assert.Single(factory.CreateQueryResults(query, data, "gh org"));

        proxy.Language = "en";
        var refreshedResult = Assert.Single(factory.CreateQueryResults(query, data, "gh org"));
        var menu = factory.CreateContextMenus(oldResult);

        Assert.Contains("Cached", refreshedResult.SubTitle);
        Assert.Contains("GitHub organization", refreshedResult.SubTitle);
        Assert.Contains(menu, result => result.Title == "Browse this organization's repositories");
        Assert.Contains(menu, result => result.Title == "Copy URL");
        Assert.Equal("Acme", refreshedResult.Title);

        proxy.Language = "zh-tw";
        Assert.Contains(factory.CreateContextMenus(oldResult), result => result.Title == "查看此組織的儲存庫");
    }

    [Fact]
    public void QueryErrors_AreTranslatedAtDisplayTimeAndKeepArguments()
    {
        var (factory, proxy) = CreateFactory();
        var invalid = QueryParser.Parse("home extra");
        var timeout = new GitHubQueryResult([], GitHubErrorKind.Timeout,
            Localization.Prefix + "runner_timeout", ErrorMessageArguments: [5]);

        proxy.Language = "en";
        Assert.Contains("does not accept arguments", Assert.Single(factory.CreateInvalidQueryResult(invalid)).SubTitle);
        var english = Assert.Single(factory.CreateQueryResults(new ParsedQuery(QueryKind.MyRepositories), timeout, "gh repo"));
        Assert.Equal("GitHub CLI query timed out", english.Title);
        Assert.Contains("5 seconds", english.SubTitle);

        proxy.Language = "zh-cn";
        Assert.Contains("5 秒", Assert.Single(factory.CreateQueryResults(new ParsedQuery(QueryKind.MyRepositories), timeout, "gh repo")).SubTitle);
    }

    [Fact]
    public void PullRequestLabels_AreTranslatedButRemoteContentIsPreserved()
    {
        var (factory, proxy) = CreateFactory();
        proxy.Language = "en";
        var item = new GitHubItem(GitHubItemKind.PullRequest, "原始标题", "owner/repo", "https://github.com/owner/repo/pull/7",
            Description: "原始描述", Number: 7, State: "OPEN", IsDraft: true, Author: "octocat",
            Relationship: GitHubRelationship.ReviewRequested, ReviewDecision: "CHANGES_REQUESTED");

        var result = Assert.Single(factory.CreateQueryResults(new ParsedQuery(QueryKind.MyWork), new GitHubQueryResult([item]), "gh me"));

        Assert.Contains("原始标题", result.Title);
        Assert.Contains("原始描述", result.SubTitle);
        Assert.Contains("Draft", result.SubTitle);
        Assert.Contains("Open", result.SubTitle);
        Assert.Contains("Awaiting my review", result.SubTitle);
        Assert.Contains("Review Changes requested", result.SubTitle);
        Assert.Contains("Author octocat", result.SubTitle);
    }

    [Fact]
    public void EmptyResults_AndRawCliErrors_UseCurrentLanguage()
    {
        var (factory, proxy) = CreateFactory();
        proxy.Language = "en";
        var query = new ParsedQuery(QueryKind.RepositorySearch);

        Assert.Equal("No matching repositories were found", Assert.Single(factory.CreateQueryResults(query, new GitHubQueryResult([]), "gh repo")).Title);
        var error = Assert.Single(factory.CreateQueryResults(query, new GitHubQueryResult([], GitHubErrorKind.CommandFailed, "network failed"), "gh repo"));
        Assert.Contains("network failed", error.SubTitle);
        Assert.Contains("Press Enter to retry", error.SubTitle);
        Assert.Equal("GitHub CLI query failed", error.Title);
    }

    [Fact]
    public async Task PluginDescription_FollowsHostLanguage()
    {
        var api = DispatchProxy.Create<IPublicAPI, TranslationApiProxy>();
        var proxy = (TranslationApiProxy)(object)api;
        var plugin = new Main();
        await plugin.InitAsync(new PluginInitContext(new PluginMetadata(), api));

        Assert.Contains("本机", plugin.GetTranslatedPluginDescription());
        proxy.Language = "en";
        Assert.StartsWith("Search repositories", plugin.GetTranslatedPluginDescription());
        proxy.Language = "zh-tw";
        Assert.Contains("本機", plugin.GetTranslatedPluginDescription());
    }

    [Fact]
    public void AllLanguageResources_HaveMatchingKeysAndFormatParameters()
    {
        var english = TranslationApiProxy.Load("en");
        foreach (var language in new[] { "zh-cn", "zh-tw" })
        {
            var translations = TranslationApiProxy.Load(language);
            Assert.Equal(english.Keys.Order(), translations.Keys.Order());
            foreach (var (key, template) in english)
            {
                Assert.False(string.IsNullOrWhiteSpace(translations[key]));
                Assert.Equal(Parameters(template), Parameters(translations[key]));
                _ = string.Format(translations[key], 42);
            }
        }
    }

    private static string[] Parameters(string template) =>
        Regex.Matches(template, @"\{\d+(?::[^}]+)?\}").Select(match => match.Value).Order().ToArray();

    private static (ResultFactory Factory, TranslationApiProxy Proxy) CreateFactory()
    {
        var api = DispatchProxy.Create<IPublicAPI, TranslationApiProxy>();
        return (new ResultFactory(api), (TranslationApiProxy)(object)api);
    }
}
