using System.Reflection;
using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.GitHubCli;
using Xunit;

namespace Flow.Launcher.Plugin.GitHubCli.Tests;

public sealed class ResultFactoryTests
{
    [Fact]
    public void CreateHomeResults_Organizations_ExplainsRepositoryQuery()
    {
        var api = DispatchProxy.Create<IPublicAPI, TranslationApiProxy>();
        var factory = new ResultFactory(api);

        var result = Assert.Single(
            factory.CreateHomeResults(),
            item => item.Title == "我的组织");

        Assert.Equal("gh org ", result.AutoCompleteText);
        Assert.Contains("输入组织名可查看其仓库", result.SubTitle);
    }

    [Fact]
    public void CreateContextMenus_Organization_UsesOrganizationRepositoryQuery()
    {
        var api = DispatchProxy.Create<IPublicAPI, TranslationApiProxy>();
        var factory = new ResultFactory(api);
        var organization = new GitHubItem(
            GitHubItemKind.Organization,
            "Acme",
            "Acme",
            "https://github.com/Acme");

        var result = Assert.Single(factory.CreateQueryResults(
            new ParsedQuery(QueryKind.Organizations),
            new GitHubQueryResult([organization]),
            "gh org "));
        var contextQuery = Assert.Single(
            factory.CreateContextMenus(result),
            item => item.Title == "查看该组织的仓库");

        Assert.Equal("gh org Acme ", contextQuery.AutoCompleteText);
        Assert.Equal("gh org Acme", contextQuery.SubTitle);
        Assert.Equal("https://github.com/Acme", ((ContextItem)result.ContextData).Url);
    }

}
