using Flow.Launcher.Plugin.GitHubCli;
using Xunit;

namespace Flow.Launcher.Plugin.GitHubCli.Tests;

public sealed class GitHubServiceTests
{
    private static readonly DateTimeOffset FixedNow =
        new(2026, 9, 10, 20, 34, 56, TimeSpan.FromHours(8));

    [Fact]
    public async Task ExecuteAsync_MyRepositories_UsesVerifiedArgumentsAndNormalizesJson()
    {
        var runner = new RecordingRunner((_, _) => Success(RepositoryListJson));
        var service = CreateService(runner);

        var result = await service.ExecuteAsync(
            new ParsedQuery(QueryKind.MyRepositories),
            forceRefresh: false,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Items);
        Assert.Equal(GitHubItemKind.Repository, item.Kind);
        Assert.Equal("sample/project", item.Repository);
        Assert.Equal("sample/project", item.Title);
        Assert.Equal("https://github.com/sample/project", item.Url);
        Assert.Equal("A repository", item.Description);
        Assert.Equal(12, item.StarCount);
        Assert.Equal(3, item.ForkCount);
        Assert.Equal("C#", item.Language);
        Assert.Equal("PRIVATE", item.Visibility);
        Assert.Equal("ADMIN", item.ViewerPermission);
        Assert.True(item.IsFork);
        Assert.False(item.IsArchived);

        Assert.Equal(
            ["repo", "list", "--limit", "100"],
            runner.SingleCall.Take(4));
        AssertOptionValue(runner.SingleCall, "--json", RepositoryListFields);
    }

    [Fact]
    public async Task ExecuteAsync_RepositorySearch_PassesQueryAsOneArgumentAndNormalizesJson()
    {
        var runner = new RecordingRunner((_, _) => Success(RepositorySearchJson));
        var service = CreateService(runner);
        const string queryText = "-topic:archived; calc";

        var result = await service.ExecuteAsync(
            new ParsedQuery(QueryKind.RepositorySearch, SearchText: queryText),
            forceRefresh: false,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("public/example", item.Repository);
        Assert.Equal(987, item.StarCount);
        Assert.Equal(45, item.ForkCount);
        Assert.Equal("TypeScript", item.Language);
        Assert.Equal("PUBLIC", item.Visibility);

        Assert.Equal("search", runner.SingleCall[0]);
        Assert.Equal("repos", runner.SingleCall[1]);
        Assert.Contains("--", runner.SingleCall);
        Assert.Equal(queryText, runner.SingleCall[^1]);
        AssertOptionValue(runner.SingleCall, "--limit", "30");
        AssertOptionValue(runner.SingleCall, "--json", RepositorySearchFields);
    }

    [Fact]
    public async Task ExecuteAsync_RepositoryPullRequests_UsesRepositoryContext()
    {
        var runner = new RecordingRunner((_, _) => Success(PullRequestListJson));
        var service = CreateService(runner);

        var result = await service.ExecuteAsync(
            new ParsedQuery(
                QueryKind.PullRequests,
                SearchText: "fix bug",
                Repository: "owner/repo"),
            forceRefresh: false,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal(GitHubItemKind.PullRequest, item.Kind);
        Assert.Equal("owner/repo", item.Repository);
        Assert.Equal("Fix bug", item.Title);
        Assert.Equal(42, item.Number);
        Assert.Equal("OPEN", item.State);
        Assert.Equal("REVIEW_REQUIRED", item.ReviewDecision);
        Assert.Null(item.Author);

        Assert.Equal(["pr", "list"], runner.SingleCall.Take(2));
        AssertOptionValue(runner.SingleCall, "--repo", "owner/repo");
        AssertOptionValue(runner.SingleCall, "--state", "open");
        AssertOptionValue(runner.SingleCall, "--search", "fix bug");
        AssertOptionValue(runner.SingleCall, "--json", PullRequestListFields);
    }

    [Fact]
    public async Task ExecuteAsync_GlobalPullRequests_NormalizesRepositoryAndState()
    {
        var runner = new RecordingRunner((_, _) => Success(PullRequestSearchJson));
        var service = CreateService(runner);

        var result = await service.ExecuteAsync(
            new ParsedQuery(QueryKind.PullRequests, SearchText: "label:bug"),
            forceRefresh: false,
            CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("global/project", item.Repository);
        Assert.Equal("OPEN", item.State);
        Assert.Equal("octocat", item.Author);
        Assert.Equal(GitHubRelationship.None, item.Relationship);

        Assert.Equal(["search", "prs"], runner.SingleCall.Take(2));
        AssertOptionValue(runner.SingleCall, "--state", "open");
        AssertOptionValue(runner.SingleCall, "--sort", "updated");
        AssertOptionValue(runner.SingleCall, "--order", "desc");
        Assert.Equal("label:bug", runner.SingleCall[^1]);
    }

    [Fact]
    public async Task ExecuteAsync_MyWork_CombinesAndLabelsBothQueries()
    {
        var runner = new RecordingRunner((arguments, _) =>
        {
            if (arguments.Contains("--author", StringComparer.Ordinal))
            {
                return Success(AuthoredPullRequestSearchJson);
            }

            Assert.Contains("--review-requested", arguments);
            return Success(ReviewPullRequestSearchJson);
        });
        var service = CreateService(runner);

        var result = await service.ExecuteAsync(
            new ParsedQuery(QueryKind.MyWork, SearchText: "is:open"),
            forceRefresh: false,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal("Review this", result.Items[0].Title);
        Assert.Equal(GitHubRelationship.ReviewRequested, result.Items[0].Relationship);
        Assert.Equal("My change", result.Items[1].Title);
        Assert.Equal(GitHubRelationship.Authored, result.Items[1].Relationship);
        Assert.Equal(2, runner.Calls.Count);

        var authoredCall = Assert.Single(
            runner.Calls,
            call => call.Contains("--author", StringComparer.Ordinal));
        AssertOptionValue(authoredCall, "--author", "@me");
        Assert.Equal("is:open", authoredCall[^1]);

        var reviewCall = Assert.Single(
            runner.Calls,
            call => call.Contains("--review-requested", StringComparer.Ordinal));
        AssertOptionValue(reviewCall, "--review-requested", "@me");
    }

    [Fact]
    public async Task ExecuteAsync_Organizations_FiltersLocallyAndSharesCache()
    {
        var runner = new RecordingRunner((_, _) => Success("""
            Alpha	https://github.example/Alpha
            Beta-Team	https://github.example/Beta-Team
            alpha	https://github.example/alpha
            """));
        var service = CreateService(runner);

        var first = await service.ExecuteAsync(
            new ParsedQuery(QueryKind.Organizations, SearchText: "team"),
            forceRefresh: false,
            CancellationToken.None);
        var second = await service.ExecuteAsync(
            new ParsedQuery(QueryKind.Organizations, SearchText: "alp"),
            forceRefresh: false,
            CancellationToken.None);

        var firstItem = Assert.Single(first.Items);
        Assert.Equal(GitHubItemKind.Organization, firstItem.Kind);
        Assert.Equal("Beta-Team", firstItem.Title);
        Assert.Equal("Beta-Team", firstItem.Repository);
        Assert.Equal("https://github.example/Beta-Team", firstItem.Url);

        var secondItem = Assert.Single(second.Items);
        Assert.Equal("Alpha", secondItem.Title);
        Assert.True(second.IsFromCache);
        Assert.Single(runner.Calls);
        Assert.Equal(
            [
                "api",
                "graphql",
                "--paginate",
                "--raw-field",
                "query=query($endCursor: String) { viewer { organizations(first: 100, after: $endCursor) { nodes { login url } pageInfo { hasNextPage endCursor } } } }",
                "--jq",
                ".data.viewer.organizations.nodes[] | [.login, .url] | @tsv",
            ],
            runner.SingleCall);
    }

    [Fact]
    public async Task ExecuteAsync_OrganizationRepositories_UsesOwnerAndFiltersCachedResults()
    {
        var runner = new RecordingRunner((_, _) => Success(OrganizationRepositoryListJson));
        var service = CreateService(runner);

        var first = await service.ExecuteAsync(
            new ParsedQuery(
                QueryKind.OrganizationRepositories,
                SearchText: "service",
                Organization: "Acme"),
            forceRefresh: false,
            CancellationToken.None);
        var second = await service.ExecuteAsync(
            new ParsedQuery(
                QueryKind.OrganizationRepositories,
                SearchText: "desktop launcher",
                Organization: "Acme"),
            forceRefresh: false,
            CancellationToken.None);

        var firstItem = Assert.Single(first.Items);
        Assert.Equal("Acme/service-api", firstItem.Repository);
        Assert.Equal("Backend", firstItem.Description);

        var secondItem = Assert.Single(second.Items);
        Assert.Equal("Acme/flow-plugin", secondItem.Repository);
        Assert.True(second.IsFromCache);
        Assert.Single(runner.Calls);
        Assert.Equal(["repo", "list", "Acme", "--limit", "100"], runner.SingleCall.Take(5));
        AssertOptionValue(runner.SingleCall, "--json", RepositoryListFields);
    }

    [Fact]
    public async Task ExecuteAsync_Trend_UsesUtcCutoffLanguageAndVerifiedSorting()
    {
        var runner = new RecordingRunner((_, _) => Success(RepositorySearchJson));
        var service = CreateService(runner);

        var result = await service.ExecuteAsync(
            new ParsedQuery(
                QueryKind.Trend,
                TrendPeriod: TrendPeriod.Weekly,
                Language: "Jupyter Notebook"),
            forceRefresh: false,
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        AssertOptionValue(
            runner.SingleCall,
            "--created",
            ">=2026-09-03T12:34:56Z");
        AssertOptionValue(runner.SingleCall, "--sort", "stars");
        AssertOptionValue(runner.SingleCall, "--order", "desc");
        AssertOptionValue(runner.SingleCall, "--visibility", "public");
        Assert.Contains("--include-forks=false", runner.SingleCall);
        AssertOptionValue(runner.SingleCall, "--language", "Jupyter Notebook");
    }

    [Fact]
    public async Task ExecuteAsync_DirectTargets_UseViewCommands()
    {
        var runner = new RecordingRunner((arguments, _) =>
            arguments[0] == "repo"
                ? Success(DirectRepositoryJson)
                : Success(DirectPullRequestJson));
        var service = CreateService(runner);

        var repository = await service.ExecuteAsync(
            new ParsedQuery(QueryKind.DirectRepository, Repository: "owner/repo"),
            forceRefresh: false,
            CancellationToken.None);
        var pullRequest = await service.ExecuteAsync(
            new ParsedQuery(
                QueryKind.DirectPullRequest,
                Repository: "owner/repo",
                PullRequestNumber: 7),
            forceRefresh: false,
            CancellationToken.None);

        Assert.Equal("owner/repo", Assert.Single(repository.Items).Repository);
        Assert.Equal(7, Assert.Single(pullRequest.Items).Number);
        Assert.Equal(["repo", "view", "owner/repo"], runner.Calls[0].Take(3));
        Assert.Equal(["pr", "view", "7", "--repo", "owner/repo"], runner.Calls[1].Take(5));
    }

    [Fact]
    public async Task ExecuteAsync_CacheExpiresAndForceRefreshBypassesIt()
    {
        var now = FixedNow;
        var runner = new RecordingRunner((_, _) => Success(RepositorySearchJson));
        var service = new GitHubService(runner, () => now);
        var query = new ParsedQuery(QueryKind.RepositorySearch, SearchText: "flow");

        var first = await service.ExecuteAsync(query, forceRefresh: false, CancellationToken.None);
        now = now.AddSeconds(29);
        var cached = await service.ExecuteAsync(query, forceRefresh: false, CancellationToken.None);
        var refreshed = await service.ExecuteAsync(query, forceRefresh: true, CancellationToken.None);
        now = now.AddSeconds(31);
        var expired = await service.ExecuteAsync(query, forceRefresh: false, CancellationToken.None);

        Assert.False(first.IsFromCache);
        Assert.True(cached.IsFromCache);
        Assert.False(refreshed.IsFromCache);
        Assert.False(expired.IsFromCache);
        Assert.Equal(3, runner.Calls.Count);
    }

    [Fact]
    public async Task ExecuteAsync_ExpiredCacheFallsBackToStaleOnCommandFailure()
    {
        var now = FixedNow;
        var callCount = 0;
        var runner = new RecordingRunner((_, _) =>
            Interlocked.Increment(ref callCount) == 1
                ? Success(RepositorySearchJson)
                : new GhCommandResult(1, string.Empty, "network failed"));
        var service = new GitHubService(runner, () => now);
        var query = new ParsedQuery(QueryKind.RepositorySearch, SearchText: "flow");

        _ = await service.ExecuteAsync(query, forceRefresh: false, CancellationToken.None);
        now = now.AddSeconds(31);
        var result = await service.ExecuteAsync(query, forceRefresh: false, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(GitHubErrorKind.CommandFailed, result.ErrorKind);
        Assert.True(result.IsFromCache);
        Assert.True(result.IsStale);
        Assert.Equal("network failed", result.ErrorMessage);
        Assert.Single(result.Items);
    }

    [Theory]
    [InlineData(4, (int)GitHubErrorKind.AuthenticationRequired)]
    [InlineData(1, (int)GitHubErrorKind.CommandFailed)]
    public async Task ExecuteAsync_NonZeroExitCode_ReturnsTypedError(
        int exitCode,
        int expectedError)
    {
        var runner = new RecordingRunner((_, _) =>
            new GhCommandResult(exitCode, string.Empty, "failure"));
        var service = CreateService(runner);

        var result = await service.ExecuteAsync(
            new ParsedQuery(QueryKind.MyRepositories),
            forceRefresh: false,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal((GitHubErrorKind)expectedError, result.ErrorKind);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidJson_ReturnsInvalidResponse()
    {
        var runner = new RecordingRunner((_, _) => Success("not-json"));
        var service = CreateService(runner);

        var result = await service.ExecuteAsync(
            new ParsedQuery(QueryKind.MyRepositories),
            forceRefresh: false,
            CancellationToken.None);

        Assert.Equal(GitHubErrorKind.InvalidResponse, result.ErrorKind);
    }

    [Fact]
    public async Task ExecuteAsync_RunnerFailure_ReturnsTypedError()
    {
        var runner = new RecordingRunner((_, _) =>
            throw new GitHubCliException(GitHubCliFailureKind.NotFound, "missing gh"));
        var service = CreateService(runner);

        var result = await service.ExecuteAsync(
            new ParsedQuery(QueryKind.MyRepositories),
            forceRefresh: false,
            CancellationToken.None);

        Assert.Equal(GitHubErrorKind.CliUnavailable, result.ErrorKind);
        Assert.Equal("missing gh", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationPropagates()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var runner = new RecordingRunner((_, token) =>
            throw new OperationCanceledException(token));
        var service = CreateService(runner);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ExecuteAsync(
                new ParsedQuery(QueryKind.MyRepositories),
                forceRefresh: false,
                cancellationSource.Token));
    }

    private static GitHubService CreateService(RecordingRunner runner) =>
        new(runner, () => FixedNow);

    private static GhCommandResult Success(string output) =>
        new(0, output, string.Empty);

    private static void AssertOptionValue(
        IReadOnlyList<string> arguments,
        string option,
        string expectedValue)
    {
        var index = arguments.IndexOf(option);
        Assert.True(index >= 0, $"Missing option {option}.");
        Assert.True(index + 1 < arguments.Count, $"Missing value for option {option}.");
        Assert.Equal(expectedValue, arguments[index + 1]);
    }

    private const string RepositoryListFields =
        "nameWithOwner,description,visibility,isPrivate,isArchived,isFork,stargazerCount,forkCount,primaryLanguage,createdAt,updatedAt,pushedAt,url,viewerPermission";

    private const string RepositorySearchFields =
        "fullName,description,visibility,isPrivate,isArchived,isFork,stargazersCount,forksCount,language,createdAt,updatedAt,pushedAt,url";

    private const string PullRequestListFields =
        "number,title,state,isDraft,author,createdAt,updatedAt,url,reviewDecision";

    private const string RepositoryListJson = """
        [{
          "nameWithOwner": "sample/project",
          "description": "A repository",
          "visibility": "PRIVATE",
          "isPrivate": true,
          "isArchived": false,
          "isFork": true,
          "stargazerCount": 12,
          "forkCount": 3,
          "primaryLanguage": { "name": "C#" },
          "createdAt": "2026-01-02T03:04:05Z",
          "updatedAt": "2026-09-09T10:11:12Z",
          "pushedAt": "2026-09-08T09:10:11Z",
          "url": "https://github.com/sample/project",
          "viewerPermission": "ADMIN"
        }]
        """;

    private const string RepositorySearchJson = """
        [{
          "fullName": "public/example",
          "description": null,
          "visibility": "public",
          "isPrivate": false,
          "isArchived": false,
          "isFork": false,
          "stargazersCount": 987,
          "forksCount": 45,
          "language": "TypeScript",
          "createdAt": "2026-09-01T01:02:03Z",
          "updatedAt": "2026-09-10T11:12:13Z",
          "pushedAt": null,
          "url": "https://github.com/public/example"
        }]
        """;

    private const string OrganizationRepositoryListJson = """
        [
          {
            "nameWithOwner": "Acme/service-api",
            "description": "Backend",
            "visibility": "PRIVATE",
            "isPrivate": true,
            "isArchived": false,
            "isFork": false,
            "stargazerCount": 4,
            "forkCount": 1,
            "primaryLanguage": { "name": "C#" },
            "createdAt": "2026-01-02T03:04:05Z",
            "updatedAt": "2026-09-09T10:11:12Z",
            "pushedAt": "2026-09-08T09:10:11Z",
            "url": "https://github.com/Acme/service-api",
            "viewerPermission": "WRITE"
          },
          {
            "nameWithOwner": "Acme/flow-plugin",
            "description": "Desktop launcher integration",
            "visibility": "PUBLIC",
            "isPrivate": false,
            "isArchived": false,
            "isFork": false,
            "stargazerCount": 10,
            "forkCount": 2,
            "primaryLanguage": { "name": "C#" },
            "createdAt": "2026-02-02T03:04:05Z",
            "updatedAt": "2026-09-10T10:11:12Z",
            "pushedAt": "2026-09-10T09:10:11Z",
            "url": "https://github.com/Acme/flow-plugin",
            "viewerPermission": "READ"
          }
        ]
        """;

    private const string PullRequestListJson = """
        [{
          "number": 42,
          "title": "Fix bug",
          "state": "OPEN",
          "isDraft": false,
          "author": null,
          "createdAt": "2026-09-01T01:02:03Z",
          "updatedAt": "2026-09-10T11:12:13Z",
          "url": "https://github.com/owner/repo/pull/42",
          "reviewDecision": "REVIEW_REQUIRED"
        }]
        """;

    private const string PullRequestSearchJson = """
        [{
          "number": 8,
          "title": "Global change",
          "state": "open",
          "isDraft": true,
          "author": { "login": "octocat" },
          "repository": { "name": "project", "nameWithOwner": "global/project" },
          "createdAt": "2026-09-02T01:02:03Z",
          "updatedAt": "2026-09-09T11:12:13Z",
          "url": "https://github.com/global/project/pull/8"
        }]
        """;

    private const string AuthoredPullRequestSearchJson = """
        [{
          "number": 10,
          "title": "My change",
          "state": "open",
          "isDraft": false,
          "author": { "login": "me" },
          "repository": { "nameWithOwner": "mine/project" },
          "createdAt": "2026-09-01T01:02:03Z",
          "updatedAt": "2026-09-08T11:12:13Z",
          "url": "https://github.com/mine/project/pull/10"
        }]
        """;

    private const string ReviewPullRequestSearchJson = """
        [{
          "number": 11,
          "title": "Review this",
          "state": "open",
          "isDraft": false,
          "author": { "login": "teammate" },
          "repository": { "nameWithOwner": "team/project" },
          "createdAt": "2026-09-03T01:02:03Z",
          "updatedAt": "2026-09-10T11:12:13Z",
          "url": "https://github.com/team/project/pull/11"
        }]
        """;

    private const string DirectRepositoryJson = """
        {
          "nameWithOwner": "owner/repo",
          "description": null,
          "visibility": "PUBLIC",
          "isPrivate": false,
          "isArchived": false,
          "isFork": false,
          "stargazerCount": 1,
          "forkCount": 0,
          "primaryLanguage": null,
          "createdAt": "2026-01-01T00:00:00Z",
          "updatedAt": "2026-09-01T00:00:00Z",
          "pushedAt": null,
          "url": "https://github.com/owner/repo",
          "viewerPermission": "READ"
        }
        """;

    private const string DirectPullRequestJson = """
        {
          "number": 7,
          "title": "Direct PR",
          "state": "OPEN",
          "isDraft": false,
          "author": { "login": "author" },
          "createdAt": "2026-09-01T00:00:00Z",
          "updatedAt": "2026-09-02T00:00:00Z",
          "url": "https://github.com/owner/repo/pull/7",
          "reviewDecision": "APPROVED"
        }
        """;

    private sealed class RecordingRunner(
        Func<IReadOnlyList<string>, CancellationToken, GhCommandResult> handler)
        : IGitHubCliRunner
    {
        private readonly object _gate = new();
        private readonly List<string[]> _calls = [];

        internal IReadOnlyList<string[]> Calls
        {
            get
            {
                lock (_gate)
                {
                    return _calls.ToArray();
                }
            }
        }

        internal string[] SingleCall => Assert.Single(Calls);

        public Task<GhCommandResult> RunAsync(
            IReadOnlyList<string> arguments,
            CancellationToken cancellationToken)
        {
            var argumentsCopy = arguments.ToArray();
            lock (_gate)
            {
                _calls.Add(argumentsCopy);
            }

            return Task.FromResult(handler(argumentsCopy, cancellationToken));
        }
    }
}

internal static class ReadOnlyListTestExtensions
{
    internal static int IndexOf(this IReadOnlyList<string> values, string expected)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (string.Equals(values[index], expected, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
