using Flow.Launcher.Plugin;

namespace Flow.Launcher.Plugin.GitHubCli;

internal sealed class ResultFactory
{
    private const string IconPath = "Images\\app.png";
    private const string HomeQuery = "gh ";
    private const int FirstItemScore = 1_000;

    private readonly IPublicAPI _api;

    internal ResultFactory(IPublicAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
    }

    internal List<Result> CreateHomeResults()
    {
        return
        [
            CreateNavigationResult(
                "我的仓库",
                "列出当前 gh 账号拥有的仓库",
                "gh repo ",
                FirstItemScore),
            CreateNavigationResult(
                "我的组织",
                "列出当前 gh 账号加入的组织，可继续输入关键词筛选",
                "gh org ",
                FirstItemScore - 1),
            CreateNavigationResult(
                "Pull Requests",
                "搜索仓库中的 Pull Request，或跨仓库搜索",
                "gh pr ",
                FirstItemScore - 2),
            CreateNavigationResult(
                "我的工作",
                "查看我创建的以及等待我审阅的 Pull Request",
                "gh me ",
                FirstItemScore - 3),
            CreateNavigationResult(
                "GitHub Trend",
                "查看近 1/7/30 天新建且当前 Star 较高的仓库",
                "gh trend ",
                FirstItemScore - 4),
        ];
    }

    internal List<Result> CreateInvalidQueryResult(ParsedQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        return
        [
            CreateErrorResult(
                GitHubErrorKind.InvalidQuery,
                query.ErrorMessage,
                HomeQuery,
                hasStaleItems: false),
        ];
    }

    internal List<Result> CreateQueryResults(
        ParsedQuery query,
        GitHubQueryResult queryResult,
        string retryQuery)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(queryResult);

        var results = new List<Result>(queryResult.Items.Count + 1);

        if (queryResult.ErrorKind is { } errorKind)
        {
            results.Add(CreateErrorResult(
                errorKind,
                queryResult.ErrorMessage,
                retryQuery,
                queryResult.IsStale && queryResult.Items.Count > 0));
        }

        var itemScoreOffset = results.Count;
        for (var index = 0; index < queryResult.Items.Count; index++)
        {
            results.Add(CreateItemResult(
                query,
                queryResult.Items[index],
                queryResult.IsFromCache,
                queryResult.IsStale,
                FirstItemScore - index - itemScoreOffset));
        }

        if (results.Count == 0)
        {
            results.Add(CreateEmptyResult(query));
        }

        return results;
    }

    internal List<Result> CreateUnexpectedErrorResult(string retryQuery)
    {
        return
        [
            CreateErrorResult(
                GitHubErrorKind.InvalidResponse,
                "插件处理查询时发生意外错误。",
                retryQuery,
                hasStaleItems: false),
        ];
    }

    internal List<Result> CreateContextMenus(Result selectedResult)
    {
        ArgumentNullException.ThrowIfNull(selectedResult);

        if (selectedResult.ContextData is not ContextItem contextItem)
        {
            return [];
        }

        var results = new List<Result>(4);

        if (!string.IsNullOrWhiteSpace(contextItem.Url))
        {
            var url = contextItem.Url;
            results.Add(new Result
            {
                Title = contextItem.OpenTitle ?? "在 GitHub 中打开",
                SubTitle = url,
                IcoPath = IconPath,
                RecordKey = $"context:open:{url}",
                AsyncAction = _ =>
                {
                    _api.OpenUrl(url);
                    return ValueTask.FromResult(true);
                },
            });

            results.Add(new Result
            {
                Title = "复制 URL",
                SubTitle = url,
                IcoPath = IconPath,
                RecordKey = $"context:copy-url:{url}",
                AsyncAction = _ =>
                {
                    _api.CopyToClipboard(url);
                    return ValueTask.FromResult(true);
                },
            });
        }

        if (!string.IsNullOrWhiteSpace(contextItem.Identifier))
        {
            var identifier = contextItem.Identifier;
            results.Add(new Result
            {
                Title = "复制标识",
                SubTitle = identifier,
                IcoPath = IconPath,
                RecordKey = $"context:copy-identifier:{identifier}",
                AsyncAction = _ =>
                {
                    _api.CopyToClipboard(identifier);
                    return ValueTask.FromResult(true);
                },
            });
        }

        if (!string.IsNullOrWhiteSpace(contextItem.QueryText))
        {
            var queryText = contextItem.QueryText;
            results.Add(new Result
            {
                Title = contextItem.QueryTitle ?? "切换查询",
                SubTitle = queryText.TrimEnd(),
                AutoCompleteText = queryText,
                IcoPath = IconPath,
                RecordKey = $"context:query:{queryText}",
                AsyncAction = _ =>
                {
                    _api.ChangeQuery(queryText, contextItem.ForceRequery);
                    return ValueTask.FromResult(false);
                },
            });
        }

        return results;
    }

    private Result CreateNavigationResult(
        string title,
        string subtitle,
        string queryText,
        int score)
    {
        return new Result
        {
            Title = title,
            SubTitle = subtitle,
            AutoCompleteText = queryText,
            IcoPath = IconPath,
            Score = score,
            AddSelectedCount = false,
            RecordKey = $"home:{queryText.Trim()}",
            ContextData = new ContextItem(
                QueryText: queryText,
                QueryTitle: "使用此查询"),
            AsyncAction = _ =>
            {
                _api.ChangeQuery(queryText);
                return ValueTask.FromResult(false);
            },
        };
    }

    private Result CreateItemResult(
        ParsedQuery query,
        GitHubItem item,
        bool isFromCache,
        bool isStale,
        int score)
    {
        var identifier = item.Kind == GitHubItemKind.PullRequest && item.Number is { } number
            ? $"{item.Repository}#{number}"
            : item.Repository;
        var queryText = item.Kind == GitHubItemKind.Organization
            ? $"gh repo owner:{item.Repository} "
            : $"gh pr {item.Repository} ";
        var queryTitle = item.Kind == GitHubItemKind.Organization
            ? "查看该组织的仓库"
            : "查看该仓库的 Pull Requests";
        var autoCompleteText = item.Kind == GitHubItemKind.Organization
            ? $"gh org {item.Repository}"
            : $"gh {identifier}";

        return new Result
        {
            Title = CreateItemTitle(item),
            SubTitle = CreateItemSubtitle(query, item, isFromCache, isStale),
            AutoCompleteText = autoCompleteText,
            CopyText = item.Url,
            IcoPath = IconPath,
            Score = score,
            AddSelectedCount = false,
            RecordKey = item.Url,
            ContextData = new ContextItem(
                Url: item.Url,
                Identifier: identifier,
                QueryText: queryText,
                QueryTitle: queryTitle),
            AsyncAction = _ =>
            {
                _api.OpenUrl(item.Url);
                return ValueTask.FromResult(true);
            },
        };
    }

    private Result CreateErrorResult(
        GitHubErrorKind errorKind,
        string? errorMessage,
        string retryQuery,
        bool hasStaleItems)
    {
        var title = hasStaleItems
            ? "刷新失败，正在显示缓存结果"
            : ErrorTitle(errorKind);
        var message = CompactMessage(errorMessage) ?? ErrorFallbackMessage(errorKind);
        var recovery = CreateRecovery(errorKind, retryQuery);

        return new Result
        {
            Title = title,
            SubTitle = $"{message} · {recovery.Hint}",
            IcoPath = IconPath,
            Score = Result.MaxScore - 1,
            AddSelectedCount = false,
            RecordKey = $"error:{errorKind}",
            ContextData = new ContextItem(
                Url: recovery.Url,
                OpenTitle: recovery.OpenTitle,
                QueryText: recovery.QueryText,
                QueryTitle: recovery.QueryTitle,
                ForceRequery: recovery.ForceRequery),
            AsyncAction = recovery.Url is { } url
                ? _ =>
                {
                    _api.OpenUrl(url);
                    return ValueTask.FromResult(true);
                }
            : _ =>
            {
                _api.ChangeQuery(recovery.QueryText ?? HomeQuery, recovery.ForceRequery);
                return ValueTask.FromResult(false);
            },
        };
    }

    private Result CreateEmptyResult(ParsedQuery query)
    {
        return new Result
        {
            Title = EmptyTitle(query.Kind),
            SubTitle = "按 Enter 返回 GitHub CLI 首页",
            IcoPath = IconPath,
            AddSelectedCount = false,
            RecordKey = $"empty:{query.Kind}",
            ContextData = new ContextItem(
                QueryText: HomeQuery,
                QueryTitle: "返回 GitHub CLI 首页"),
            AsyncAction = _ =>
            {
                _api.ChangeQuery(HomeQuery);
                return ValueTask.FromResult(false);
            },
        };
    }

    private static string CreateItemTitle(GitHubItem item)
    {
        if (item.Kind != GitHubItemKind.PullRequest || item.Number is not { } number)
        {
            return item.Title;
        }

        return $"{item.Repository} #{number} · {item.Title}";
    }

    private static string CreateItemSubtitle(
        ParsedQuery query,
        GitHubItem item,
        bool isFromCache,
        bool isStale)
    {
        var parts = new List<string>(8);

        if (isStale)
        {
            parts.Add("旧缓存");
        }
        else if (isFromCache)
        {
            parts.Add("缓存");
        }

        if (query.Kind == QueryKind.Trend)
        {
            parts.Add(query.TrendPeriod switch
            {
                TrendPeriod.Daily => "近 1 天新建",
                TrendPeriod.Monthly => "近 30 天新建",
                _ => "近 7 天新建",
            });
        }

        if (item.Kind == GitHubItemKind.PullRequest)
        {
            AddPullRequestDetails(parts, item);
        }
        else if (item.Kind == GitHubItemKind.Organization)
        {
            parts.Add("GitHub 组织");
        }
        else
        {
            AddRepositoryDetails(parts, item);
        }

        if (!string.IsNullOrWhiteSpace(item.Description))
        {
            parts.Add(item.Description.Trim());
        }

        return parts.Count > 0
            ? string.Join(" · ", parts)
            : item.Kind switch
            {
                GitHubItemKind.PullRequest => "GitHub Pull Request",
                GitHubItemKind.Organization => "GitHub 组织",
                _ => "GitHub 仓库",
            };
    }

    private static void AddPullRequestDetails(List<string> parts, GitHubItem item)
    {
        if (item.IsDraft)
        {
            parts.Add("草稿");
        }

        if (!string.IsNullOrWhiteSpace(item.State))
        {
            parts.Add(item.State.ToUpperInvariant());
        }

        if (item.Relationship == GitHubRelationship.Authored)
        {
            parts.Add("我创建的");
        }
        else if (item.Relationship == GitHubRelationship.ReviewRequested)
        {
            parts.Add("等待我审阅");
        }

        if (!string.IsNullOrWhiteSpace(item.ReviewDecision))
        {
            parts.Add($"Review {item.ReviewDecision.ToUpperInvariant()}");
        }

        if (!string.IsNullOrWhiteSpace(item.Author))
        {
            parts.Add($"作者 {item.Author}");
        }

        if (item.UpdatedAt is { } updatedAt)
        {
            parts.Add($"更新于 {FormatDate(updatedAt)}");
        }
    }

    private static void AddRepositoryDetails(List<string> parts, GitHubItem item)
    {
        if (item.IsArchived)
        {
            parts.Add("已归档");
        }

        if (!string.IsNullOrWhiteSpace(item.Visibility))
        {
            parts.Add(item.Visibility.ToUpperInvariant());
        }

        if (!string.IsNullOrWhiteSpace(item.Language))
        {
            parts.Add(item.Language);
        }

        if (item.StarCount is { } stars)
        {
            parts.Add($"★ {stars:N0}");
        }

        if (item.ForkCount is { } forks)
        {
            parts.Add($"Fork {forks:N0}");
        }

        if ((item.PushedAt ?? item.UpdatedAt) is { } activityAt)
        {
            parts.Add($"更新于 {FormatDate(activityAt)}");
        }
    }

    private static string FormatDate(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString("yyyy-MM-dd");
    }

    private static string ErrorTitle(GitHubErrorKind errorKind)
    {
        return errorKind switch
        {
            GitHubErrorKind.AuthenticationRequired => "GitHub CLI 尚未登录",
            GitHubErrorKind.CommandFailed => "GitHub CLI 查询失败",
            GitHubErrorKind.InvalidQuery => "查询格式无效",
            GitHubErrorKind.InvalidResponse => "无法解析 GitHub CLI 返回结果",
            GitHubErrorKind.CliUnavailable => "未找到 GitHub CLI",
            GitHubErrorKind.Timeout => "GitHub CLI 查询超时",
            _ => "GitHub CLI 查询失败",
        };
    }

    private static string ErrorFallbackMessage(GitHubErrorKind errorKind)
    {
        return errorKind switch
        {
            GitHubErrorKind.AuthenticationRequired => "请先使用 gh auth login 登录 GitHub。",
            GitHubErrorKind.CommandFailed => "gh 命令未能完成查询。",
            GitHubErrorKind.InvalidQuery => "请修改查询参数后重试。",
            GitHubErrorKind.InvalidResponse => "gh 返回了插件无法识别的数据。",
            GitHubErrorKind.CliUnavailable => "请安装 GitHub CLI，并确保 gh.exe 位于 PATH 中。",
            GitHubErrorKind.Timeout => "查询超过等待时间。",
            _ => "查询未能完成。",
        };
    }

    private static string EmptyTitle(QueryKind queryKind)
    {
        return queryKind switch
        {
            QueryKind.MyRepositories => "没有找到当前账号拥有的仓库",
            QueryKind.RepositorySearch => "没有找到匹配的仓库",
            QueryKind.PullRequests => "没有找到匹配的 Pull Request",
            QueryKind.MyWork => "当前没有待处理的 Pull Request",
            QueryKind.Organizations => "没有找到匹配的组织",
            QueryKind.Trend => "没有找到符合条件的 Trend 仓库",
            QueryKind.DirectRepository => "没有找到该仓库",
            QueryKind.DirectPullRequest => "没有找到该 Pull Request",
            _ => "没有找到结果",
        };
    }

    private static ErrorRecovery CreateRecovery(
        GitHubErrorKind errorKind,
        string retryQuery)
    {
        return errorKind switch
        {
            GitHubErrorKind.CliUnavailable => new ErrorRecovery(
                Hint: "按 Enter 打开安装页面",
                Url: "https://cli.github.com/",
                OpenTitle: "打开 GitHub CLI 安装页面"),
            GitHubErrorKind.AuthenticationRequired => new ErrorRecovery(
                Hint: "按 Enter 查看登录命令",
                Url: "https://cli.github.com/manual/gh_auth_login",
                OpenTitle: "查看 gh auth login 用法"),
            GitHubErrorKind.InvalidQuery => new ErrorRecovery(
                Hint: "按 Enter 返回插件首页",
                QueryText: HomeQuery,
                QueryTitle: "返回 GitHub CLI 首页"),
            _ => new ErrorRecovery(
                Hint: "按 Enter 重试",
                QueryText: retryQuery,
                QueryTitle: "重试查询",
                ForceRequery: true),
        };
    }

    private static string? CompactMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var compact = string.Join(
            ' ',
            value.Split(
                ['\r', '\n', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        const int maximumLength = 240;
        return compact.Length <= maximumLength
            ? compact
            : $"{compact[..maximumLength]}…";
    }

    private sealed record ErrorRecovery(
        string Hint,
        string? Url = null,
        string? OpenTitle = null,
        string? QueryText = null,
        string? QueryTitle = null,
        bool ForceRequery = false);
}
