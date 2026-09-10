using System.Globalization;
using System.Text.Json;

namespace Flow.Launcher.Plugin.GitHubCli;

internal sealed class GitHubService
{
    private const int RepositoryListLimit = 100;
    private const int SearchLimit = 30;
    private const int MaximumCacheEntries = 128;

    private const string RepositoryListJsonFields =
        "nameWithOwner,description,visibility,isPrivate,isArchived,isFork,stargazerCount,forkCount,primaryLanguage,createdAt,updatedAt,pushedAt,url,viewerPermission";

    private const string RepositorySearchJsonFields =
        "fullName,description,visibility,isPrivate,isArchived,isFork,stargazersCount,forksCount,language,createdAt,updatedAt,pushedAt,url";

    private const string PullRequestListJsonFields =
        "number,title,state,isDraft,author,createdAt,updatedAt,url,reviewDecision";

    private const string PullRequestSearchJsonFields =
        "number,title,state,isDraft,author,repository,createdAt,updatedAt,url";

    private const string OrganizationGraphQlQuery =
        "query($endCursor: String) { viewer { organizations(first: 100, after: $endCursor) { nodes { login url } pageInfo { hasNextPage endCursor } } } }";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IGitHubCliRunner _runner;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Dictionary<ParsedQuery, CacheEntry> _cache = [];
    private readonly object _cacheGate = new();

    internal GitHubService(IGitHubCliRunner runner)
        : this(runner, static () => DateTimeOffset.UtcNow)
    {
    }

    internal GitHubService(
        IGitHubCliRunner runner,
        Func<DateTimeOffset> utcNow)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(utcNow);

        _runner = runner;
        _utcNow = utcNow;
    }

    internal async Task<GitHubQueryResult> ExecuteAsync(
        ParsedQuery query,
        bool forceRefresh,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Kind == QueryKind.Home)
        {
            return Success([]);
        }

        if (query.Kind == QueryKind.Invalid)
        {
            return Error(
                GitHubErrorKind.InvalidQuery,
                query.ErrorMessage ?? "查询格式无效。");
        }

        var cacheQuery = CanonicalCacheQuery(query);
        var now = _utcNow().ToUniversalTime();
        var hasCachedEntry = TryGetCacheEntry(cacheQuery, out var cachedEntry);

        if (!forceRefresh
            && hasCachedEntry
            && cachedEntry.ExpiresAt > now)
        {
            return ApplyLocalFiltering(
                query,
                cachedEntry.Result with
                {
                    IsFromCache = true,
                    IsStale = false,
                });
        }

        GitHubQueryResult result;
        try
        {
            result = await FetchAsync(cacheQuery, token).ConfigureAwait(false);
        }
        catch (GitHubCliException exception)
        {
            result = Error(MapFailureKind(exception.Kind), exception.Message);
        }
        catch (JsonException)
        {
            result = Error(
                GitHubErrorKind.InvalidResponse,
                "gh 返回了无法解析的数据。请重试或在终端中检查 gh 命令输出。");
        }

        if (result.IsSuccess)
        {
            StoreCacheEntry(cacheQuery, result, _utcNow().ToUniversalTime());
            return ApplyLocalFiltering(query, result);
        }

        if (hasCachedEntry && CanUseStaleResult(result.ErrorKind))
        {
            return ApplyLocalFiltering(
                query,
                cachedEntry.Result with
                {
                    ErrorKind = result.ErrorKind,
                    ErrorMessage = result.ErrorMessage,
                    IsFromCache = true,
                    IsStale = true,
                });
        }

        return result;
    }

    private async Task<GitHubQueryResult> FetchAsync(
        ParsedQuery query,
        CancellationToken token)
    {
        return query.Kind switch
        {
            QueryKind.MyRepositories => await QueryMyRepositoriesAsync(token).ConfigureAwait(false),
            QueryKind.RepositorySearch => await SearchRepositoriesAsync(query.SearchText, token)
                .ConfigureAwait(false),
            QueryKind.PullRequests => await QueryPullRequestsAsync(query, token)
                .ConfigureAwait(false),
            QueryKind.MyWork => await QueryMyWorkAsync(query.SearchText, token)
                .ConfigureAwait(false),
            QueryKind.Organizations => await QueryOrganizationsAsync(token).ConfigureAwait(false),
            QueryKind.Trend => await QueryTrendAsync(query, token).ConfigureAwait(false),
            QueryKind.DirectRepository => await QueryDirectRepositoryAsync(query, token)
                .ConfigureAwait(false),
            QueryKind.DirectPullRequest => await QueryDirectPullRequestAsync(query, token)
                .ConfigureAwait(false),
            _ => Error(GitHubErrorKind.InvalidQuery, "不支持的查询类型。"),
        };
    }

    private async Task<GitHubQueryResult> QueryMyRepositoriesAsync(CancellationToken token)
    {
        string[] arguments =
        [
            "repo",
            "list",
            "--limit",
            RepositoryListLimit.ToString(CultureInfo.InvariantCulture),
            "--json",
            RepositoryListJsonFields,
        ];

        return await RunAndParseAsync(arguments, ParseRepositoryList, token).ConfigureAwait(false);
    }

    private async Task<GitHubQueryResult> SearchRepositoriesAsync(
        string searchText,
        CancellationToken token)
    {
        List<string> arguments =
        [
            "search",
            "repos",
            "--limit",
            SearchLimit.ToString(CultureInfo.InvariantCulture),
            "--json",
            RepositorySearchJsonFields,
        ];

        AppendSearchText(arguments, searchText);
        return await RunAndParseAsync(arguments, ParseRepositorySearch, token)
            .ConfigureAwait(false);
    }

    private async Task<GitHubQueryResult> QueryPullRequestsAsync(
        ParsedQuery query,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query.Repository))
        {
            var arguments = BuildPullRequestSearchArguments(query.SearchText);
            return await RunAndParseAsync(
                    arguments,
                    output => ParsePullRequestSearch(output, GitHubRelationship.None),
                    token)
                .ConfigureAwait(false);
        }

        List<string> repositoryArguments =
        [
            "pr",
            "list",
            "--repo",
            query.Repository,
            "--state",
            "open",
            "--limit",
            SearchLimit.ToString(CultureInfo.InvariantCulture),
            "--json",
            PullRequestListJsonFields,
        ];

        if (query.SearchText.Length > 0)
        {
            repositoryArguments.Add("--search");
            repositoryArguments.Add(query.SearchText);
        }

        return await RunAndParseAsync(
                repositoryArguments,
                output => ParsePullRequestList(output, query.Repository),
                token)
            .ConfigureAwait(false);
    }

    private async Task<GitHubQueryResult> QueryMyWorkAsync(
        string searchText,
        CancellationToken token)
    {
        var authoredArguments = BuildPullRequestSearchArguments(
            searchText,
            "--author",
            "@me");
        var reviewArguments = BuildPullRequestSearchArguments(
            searchText,
            "--review-requested",
            "@me");

        var authoredTask = _runner.RunAsync(authoredArguments, token);
        var reviewTask = _runner.RunAsync(reviewArguments, token);
        await Task.WhenAll(authoredTask, reviewTask).ConfigureAwait(false);

        var authoredCommand = await authoredTask.ConfigureAwait(false);
        var authoredError = MapCommandError(authoredCommand);
        if (authoredError is not null)
        {
            return authoredError;
        }

        var reviewCommand = await reviewTask.ConfigureAwait(false);
        var reviewError = MapCommandError(reviewCommand);
        if (reviewError is not null)
        {
            return reviewError;
        }

        var authored = ParsePullRequestSearch(
            authoredCommand.StandardOutput,
            GitHubRelationship.Authored);
        var requested = ParsePullRequestSearch(
            reviewCommand.StandardOutput,
            GitHubRelationship.ReviewRequested);

        var items = authored
            .Concat(requested)
            .OrderByDescending(item => item.UpdatedAt ?? DateTimeOffset.MinValue)
            .DistinctBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
            .Take(SearchLimit)
            .ToArray();

        return Success(items);
    }

    private async Task<GitHubQueryResult> QueryOrganizationsAsync(CancellationToken token)
    {
        string[] arguments =
        [
            "api",
            "graphql",
            "--paginate",
            "--raw-field",
            $"query={OrganizationGraphQlQuery}",
            "--jq",
            ".data.viewer.organizations.nodes[] | [.login, .url] | @tsv",
        ];

        return await RunAndParseAsync(arguments, ParseOrganizations, token).ConfigureAwait(false);
    }

    private async Task<GitHubQueryResult> QueryTrendAsync(
        ParsedQuery query,
        CancellationToken token)
    {
        if (query.TrendPeriod is null)
        {
            return Error(GitHubErrorKind.InvalidQuery, "Trend 查询缺少时间周期。");
        }

        var cutoff = _utcNow()
            .ToUniversalTime()
            .AddDays(-(int)query.TrendPeriod.Value)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        List<string> arguments =
        [
            "search",
            "repos",
            "--created",
            $">={cutoff}",
            "--sort",
            "stars",
            "--order",
            "desc",
            "--visibility",
            "public",
            "--include-forks=false",
            "--limit",
            SearchLimit.ToString(CultureInfo.InvariantCulture),
            "--json",
            RepositorySearchJsonFields,
        ];

        if (!string.IsNullOrWhiteSpace(query.Language))
        {
            arguments.Add("--language");
            arguments.Add(query.Language);
        }

        return await RunAndParseAsync(arguments, ParseRepositorySearch, token)
            .ConfigureAwait(false);
    }

    private async Task<GitHubQueryResult> QueryDirectRepositoryAsync(
        ParsedQuery query,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query.Repository))
        {
            return Error(GitHubErrorKind.InvalidQuery, "仓库直达查询缺少 owner/repo。");
        }

        string[] arguments =
        [
            "repo",
            "view",
            query.Repository,
            "--json",
            RepositoryListJsonFields,
        ];

        return await RunAndParseAsync(arguments, ParseDirectRepository, token)
            .ConfigureAwait(false);
    }

    private async Task<GitHubQueryResult> QueryDirectPullRequestAsync(
        ParsedQuery query,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query.Repository)
            || query.PullRequestNumber is null
            || query.PullRequestNumber <= 0)
        {
            return Error(GitHubErrorKind.InvalidQuery, "PR 直达查询缺少有效的 owner/repo#编号。");
        }

        string[] arguments =
        [
            "pr",
            "view",
            query.PullRequestNumber.Value.ToString(CultureInfo.InvariantCulture),
            "--repo",
            query.Repository,
            "--json",
            PullRequestListJsonFields,
        ];

        return await RunAndParseAsync(
                arguments,
                output => ParseDirectPullRequest(output, query.Repository),
                token)
            .ConfigureAwait(false);
    }

    private async Task<GitHubQueryResult> RunAndParseAsync(
        IReadOnlyList<string> arguments,
        Func<string, IReadOnlyList<GitHubItem>> parser,
        CancellationToken token)
    {
        var command = await _runner.RunAsync(arguments, token).ConfigureAwait(false);
        var error = MapCommandError(command);
        return error ?? Success(parser(command.StandardOutput));
    }

    private static List<string> BuildPullRequestSearchArguments(
        string searchText,
        params string[] additionalArguments)
    {
        List<string> arguments =
        [
            "search",
            "prs",
            "--state",
            "open",
            "--sort",
            "updated",
            "--order",
            "desc",
            "--limit",
            SearchLimit.ToString(CultureInfo.InvariantCulture),
            "--json",
            PullRequestSearchJsonFields,
        ];

        arguments.AddRange(additionalArguments);
        AppendSearchText(arguments, searchText);
        return arguments;
    }

    private static void AppendSearchText(List<string> arguments, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return;
        }

        if (searchText.StartsWith("-", StringComparison.Ordinal))
        {
            arguments.Add("--");
        }

        arguments.Add(searchText);
    }

    private static GitHubQueryResult? MapCommandError(GhCommandResult command)
    {
        if (command.ExitCode == 0)
        {
            return null;
        }

        if (command.ExitCode == 4)
        {
            return Error(
                GitHubErrorKind.AuthenticationRequired,
                "gh 尚未登录。请先在终端运行 gh auth login。");
        }

        var message = string.IsNullOrWhiteSpace(command.StandardError)
            ? $"gh 查询失败，退出码为 {command.ExitCode}."
            : command.StandardError;

        return Error(GitHubErrorKind.CommandFailed, message);
    }

    private static GitHubErrorKind MapFailureKind(GitHubCliFailureKind kind) =>
        kind switch
        {
            GitHubCliFailureKind.NotFound => GitHubErrorKind.CliUnavailable,
            GitHubCliFailureKind.TimedOut => GitHubErrorKind.Timeout,
            _ => GitHubErrorKind.CommandFailed,
        };

    private static GitHubQueryResult ApplyLocalFiltering(
        ParsedQuery originalQuery,
        GitHubQueryResult result)
    {
        if (originalQuery.Kind != QueryKind.Organizations
            || string.IsNullOrWhiteSpace(originalQuery.SearchText))
        {
            return result;
        }

        var items = result.Items
            .Where(item => item.Title.Contains(
                originalQuery.SearchText,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return result with { Items = items };
    }

    private static ParsedQuery CanonicalCacheQuery(ParsedQuery query) =>
        query.Kind == QueryKind.Organizations
            ? query with { SearchText = string.Empty }
            : query;

    private static bool CanUseStaleResult(GitHubErrorKind? errorKind) =>
        errorKind is GitHubErrorKind.CommandFailed
            or GitHubErrorKind.InvalidResponse
            or GitHubErrorKind.CliUnavailable
            or GitHubErrorKind.Timeout;

    private bool TryGetCacheEntry(ParsedQuery query, out CacheEntry entry)
    {
        lock (_cacheGate)
        {
            return _cache.TryGetValue(query, out entry!);
        }
    }

    private void StoreCacheEntry(
        ParsedQuery query,
        GitHubQueryResult result,
        DateTimeOffset storedAt)
    {
        var duration = GetCacheDuration(query.Kind);
        if (duration is null)
        {
            return;
        }

        lock (_cacheGate)
        {
            if (_cache.Count >= MaximumCacheEntries && !_cache.ContainsKey(query))
            {
                var oldest = _cache.MinBy(pair => pair.Value.StoredAt);
                _cache.Remove(oldest.Key);
            }

            _cache[query] = new CacheEntry(
                result with
                {
                    IsFromCache = false,
                    IsStale = false,
                },
                storedAt,
                storedAt + duration.Value);
        }
    }

    private static TimeSpan? GetCacheDuration(QueryKind kind) =>
        kind switch
        {
            QueryKind.MyRepositories or QueryKind.Organizations => TimeSpan.FromMinutes(5),
            QueryKind.Trend => TimeSpan.FromMinutes(15),
            QueryKind.RepositorySearch
                or QueryKind.PullRequests
                or QueryKind.MyWork
                or QueryKind.DirectRepository
                or QueryKind.DirectPullRequest => TimeSpan.FromSeconds(30),
            _ => null,
        };

    private static GitHubQueryResult Success(IReadOnlyList<GitHubItem> items) =>
        new(items);

    private static GitHubQueryResult Error(
        GitHubErrorKind kind,
        string message) =>
        new([], kind, message);

    private sealed record CacheEntry(
        GitHubQueryResult Result,
        DateTimeOffset StoredAt,
        DateTimeOffset ExpiresAt);

    private sealed class RepositoryListResponse
    {
        public string? NameWithOwner { get; init; }

        public string? Description { get; init; }

        public string? Visibility { get; init; }

        public bool IsArchived { get; init; }

        public bool IsFork { get; init; }

        public int StargazerCount { get; init; }

        public int ForkCount { get; init; }

        public LanguageResponse? PrimaryLanguage { get; init; }

        public DateTimeOffset? CreatedAt { get; init; }

        public DateTimeOffset? UpdatedAt { get; init; }

        public DateTimeOffset? PushedAt { get; init; }

        public string? Url { get; init; }

        public string? ViewerPermission { get; init; }
    }

    private sealed class RepositorySearchResponse
    {
        public string? FullName { get; init; }

        public string? Description { get; init; }

        public string? Visibility { get; init; }

        public bool IsArchived { get; init; }

        public bool IsFork { get; init; }

        public int StargazersCount { get; init; }

        public int ForksCount { get; init; }

        public string? Language { get; init; }

        public DateTimeOffset? CreatedAt { get; init; }

        public DateTimeOffset? UpdatedAt { get; init; }

        public DateTimeOffset? PushedAt { get; init; }

        public string? Url { get; init; }
    }

    private sealed class PullRequestListResponse
    {
        public int Number { get; init; }

        public string? Title { get; init; }

        public string? State { get; init; }

        public bool IsDraft { get; init; }

        public AuthorResponse? Author { get; init; }

        public DateTimeOffset? CreatedAt { get; init; }

        public DateTimeOffset? UpdatedAt { get; init; }

        public string? Url { get; init; }

        public string? ReviewDecision { get; init; }
    }

    private sealed class PullRequestSearchResponse
    {
        public int Number { get; init; }

        public string? Title { get; init; }

        public string? State { get; init; }

        public bool IsDraft { get; init; }

        public AuthorResponse? Author { get; init; }

        public RepositoryReferenceResponse? Repository { get; init; }

        public DateTimeOffset? CreatedAt { get; init; }

        public DateTimeOffset? UpdatedAt { get; init; }

        public string? Url { get; init; }
    }

    private sealed class LanguageResponse
    {
        public string? Name { get; init; }
    }

    private sealed class AuthorResponse
    {
        public string? Login { get; init; }
    }

    private sealed class RepositoryReferenceResponse
    {
        public string? NameWithOwner { get; init; }
    }

    private static IReadOnlyList<GitHubItem> ParseRepositoryList(string output) =>
        DeserializeArray<RepositoryListResponse>(output)
            .Select(MapRepositoryListItem)
            .ToArray();

    private static IReadOnlyList<GitHubItem> ParseRepositorySearch(string output) =>
        DeserializeArray<RepositorySearchResponse>(output)
            .Select(MapRepositorySearchItem)
            .ToArray();

    private static IReadOnlyList<GitHubItem> ParseDirectRepository(string output) =>
        [MapRepositoryListItem(DeserializeObject<RepositoryListResponse>(output))];

    private static IReadOnlyList<GitHubItem> ParsePullRequestList(
        string output,
        string repository) =>
        DeserializeArray<PullRequestListResponse>(output)
            .Select(item => MapPullRequestListItem(item, repository))
            .ToArray();

    private static IReadOnlyList<GitHubItem> ParseDirectPullRequest(
        string output,
        string repository) =>
        [MapPullRequestListItem(DeserializeObject<PullRequestListResponse>(output), repository)];

    private static IReadOnlyList<GitHubItem> ParsePullRequestSearch(
        string output,
        GitHubRelationship relationship) =>
        DeserializeArray<PullRequestSearchResponse>(output)
            .Select(item => MapPullRequestSearchItem(item, relationship))
            .ToArray();

    private static IReadOnlyList<GitHubItem> ParseOrganizations(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseOrganization)
            .DistinctBy(item => item.Repository, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static GitHubItem ParseOrganization(string line)
    {
        var fields = line.Split('\t', 2, StringSplitOptions.TrimEntries);
        if (fields.Length != 2)
        {
            throw new JsonException("Expected an organization login and URL.");
        }

        var organization = Required(fields[0], "organization.login");
        var url = Required(fields[1], "organization.html_url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var organizationUri)
            || organizationUri.Scheme is not ("https" or "http"))
        {
            throw new JsonException("Organization URL is invalid.");
        }

        return new GitHubItem(
            GitHubItemKind.Organization,
            organization,
            organization,
            organizationUri.AbsoluteUri);
    }

    private static GitHubItem MapRepositoryListItem(RepositoryListResponse item)
    {
        var repository = Required(item.NameWithOwner, "nameWithOwner");
        return new GitHubItem(
            GitHubItemKind.Repository,
            repository,
            repository,
            Required(item.Url, "url"),
            Description: item.Description,
            StarCount: item.StargazerCount,
            ForkCount: item.ForkCount,
            Language: item.PrimaryLanguage?.Name,
            Visibility: NormalizeUpper(item.Visibility),
            CreatedAt: item.CreatedAt,
            UpdatedAt: item.UpdatedAt,
            PushedAt: item.PushedAt,
            ViewerPermission: NormalizeUpper(item.ViewerPermission),
            IsArchived: item.IsArchived,
            IsFork: item.IsFork);
    }

    private static GitHubItem MapRepositorySearchItem(RepositorySearchResponse item)
    {
        var repository = Required(item.FullName, "fullName");
        return new GitHubItem(
            GitHubItemKind.Repository,
            repository,
            repository,
            Required(item.Url, "url"),
            Description: item.Description,
            StarCount: item.StargazersCount,
            ForkCount: item.ForksCount,
            Language: item.Language,
            Visibility: NormalizeUpper(item.Visibility),
            CreatedAt: item.CreatedAt,
            UpdatedAt: item.UpdatedAt,
            PushedAt: item.PushedAt,
            IsArchived: item.IsArchived,
            IsFork: item.IsFork);
    }

    private static GitHubItem MapPullRequestListItem(
        PullRequestListResponse item,
        string repository)
    {
        ValidatePullRequest(item.Number, item.Title, item.State, item.Url);
        return new GitHubItem(
            GitHubItemKind.PullRequest,
            item.Title!,
            repository,
            item.Url!,
            Number: item.Number,
            State: NormalizeUpper(item.State),
            IsDraft: item.IsDraft,
            Author: item.Author?.Login,
            CreatedAt: item.CreatedAt,
            UpdatedAt: item.UpdatedAt,
            ReviewDecision: NormalizeUpper(item.ReviewDecision));
    }

    private static GitHubItem MapPullRequestSearchItem(
        PullRequestSearchResponse item,
        GitHubRelationship relationship)
    {
        ValidatePullRequest(item.Number, item.Title, item.State, item.Url);
        var repository = Required(item.Repository?.NameWithOwner, "repository.nameWithOwner");
        return new GitHubItem(
            GitHubItemKind.PullRequest,
            item.Title!,
            repository,
            item.Url!,
            Number: item.Number,
            State: NormalizeUpper(item.State),
            IsDraft: item.IsDraft,
            Author: item.Author?.Login,
            Relationship: relationship,
            CreatedAt: item.CreatedAt,
            UpdatedAt: item.UpdatedAt);
    }

    private static T[] DeserializeArray<T>(string output)
    {
        return JsonSerializer.Deserialize<T[]>(output, JsonOptions)
            ?? throw new JsonException("Expected a JSON array.");
    }

    private static T DeserializeObject<T>(string output)
    {
        return JsonSerializer.Deserialize<T>(output, JsonOptions)
            ?? throw new JsonException("Expected a JSON object.");
    }

    private static void ValidatePullRequest(
        int number,
        string? title,
        string? state,
        string? url)
    {
        if (number <= 0)
        {
            throw new JsonException("Missing required field number.");
        }

        _ = Required(title, "title");
        _ = Required(state, "state");
        _ = Required(url, "url");
    }

    private static string Required(string? value, string fieldName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new JsonException($"Missing required field {fieldName}.")
            : value;

    private static string? NormalizeUpper(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.ToUpperInvariant();
}
