using Flow.Launcher.Plugin;

namespace Flow.Launcher.Plugin.GitHubCli;

internal sealed class ResultFactory
{
    private const string IconPath = "Images\\app.png";
    private const string HomeQuery = "gh ";
    private const int FirstItemScore = 1_000;

    private readonly IPublicAPI _api;
    private readonly Localization _text;

    internal ResultFactory(IPublicAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);
        _api = api;
        _text = new Localization(api);
    }

    internal List<Result> CreateHomeResults()
    {
        return
        [
            CreateNavigationResult(
                _text.Get("home_repos"),
                _text.Get("home_repos_hint"),
                "gh repo ",
                FirstItemScore),
            CreateNavigationResult(
                _text.Get("home_orgs"),
                _text.Get("home_orgs_hint"),
                "gh org ",
                FirstItemScore - 1),
            CreateNavigationResult(
                _text.Get("home_prs"),
                _text.Get("home_prs_hint"),
                "gh pr ",
                FirstItemScore - 2),
            CreateNavigationResult(
                _text.Get("home_work"),
                _text.Get("home_work_hint"),
                "gh me ",
                FirstItemScore - 3),
            CreateNavigationResult(
                _text.Get("home_trend"),
                _text.Get("home_trend_hint"),
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
                queryResult.IsStale && queryResult.Items.Count > 0,
                queryResult.ErrorMessageArguments));
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
                _text.Get("unexpected_error"),
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
                Title = _text.Message(contextItem.OpenTitle) ?? _text.Get("open_github"),
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
                Title = _text.Get("copy_url"),
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
                Title = _text.Get("copy_identifier"),
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
                Title = _text.Message(contextItem.QueryTitle) ?? _text.Get("switch_query"),
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
                QueryTitle: Localization.Prefix + "use_query"),
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
            ? $"gh org {item.Repository} "
            : $"gh pr {item.Repository} ";
        var queryTitle = item.Kind == GitHubItemKind.Organization
            ? Localization.Prefix + "org_repositories"
            : Localization.Prefix + "repo_prs";
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
        bool hasStaleItems, object[]? errorArguments = null)
    {
        var title = hasStaleItems
            ? _text.Get("stale_error")
            : ErrorTitle(errorKind);
        var message = CompactMessage(_text.Message(errorMessage, errorArguments)) ?? ErrorFallbackMessage(errorKind);
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
            SubTitle = _text.Get("home_hint"),
            IcoPath = IconPath,
            AddSelectedCount = false,
            RecordKey = $"empty:{query.Kind}",
            ContextData = new ContextItem(
                QueryText: HomeQuery,
                QueryTitle: Localization.Prefix + "go_home"),
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

    private string CreateItemSubtitle(
        ParsedQuery query,
        GitHubItem item,
        bool isFromCache,
        bool isStale)
    {
        var parts = new List<string>(8);

        if (isStale)
        {
            parts.Add(_text.Get("stale_cache"));
        }
        else if (isFromCache)
        {
            parts.Add(_text.Get("cache"));
        }

        if (query.Kind == QueryKind.Trend)
        {
            parts.Add(query.TrendPeriod switch
            {
                TrendPeriod.Daily => _text.Get("trend_daily"),
                TrendPeriod.Monthly => _text.Get("trend_monthly"),
                _ => _text.Get("trend_weekly"),
            });
        }

        if (item.Kind == GitHubItemKind.PullRequest)
        {
            AddPullRequestDetails(parts, item);
        }
        else if (item.Kind == GitHubItemKind.Organization)
        {
            parts.Add(_text.Get("organization"));
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
                GitHubItemKind.PullRequest => _text.Get("pull_request"),
                GitHubItemKind.Organization => _text.Get("organization"),
                _ => _text.Get("repository"),
            };
    }

    private void AddPullRequestDetails(List<string> parts, GitHubItem item)
    {
        if (item.IsDraft)
        {
            parts.Add(_text.Get("draft"));
        }

        if (!string.IsNullOrWhiteSpace(item.State))
        {
            parts.Add(_text.Status(item.State));
        }

        if (item.Relationship == GitHubRelationship.Authored)
        {
            parts.Add(_text.Get("authored"));
        }
        else if (item.Relationship == GitHubRelationship.ReviewRequested)
        {
            parts.Add(_text.Get("review_requested"));
        }

        if (!string.IsNullOrWhiteSpace(item.ReviewDecision))
        {
            parts.Add(_text.Format("review", _text.Status(item.ReviewDecision)));
        }

        if (!string.IsNullOrWhiteSpace(item.Author))
        {
            parts.Add(_text.Format("author", item.Author));
        }

        if (item.UpdatedAt is { } updatedAt)
        {
            parts.Add(_text.Format("updated", FormatDate(updatedAt)));
        }
    }

    private void AddRepositoryDetails(List<string> parts, GitHubItem item)
    {
        if (item.IsArchived)
        {
            parts.Add(_text.Get("archived"));
        }

        if (!string.IsNullOrWhiteSpace(item.Visibility))
        {
            parts.Add(_text.Status(item.Visibility));
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
            parts.Add(_text.Format("forks", forks));
        }

        if ((item.PushedAt ?? item.UpdatedAt) is { } activityAt)
        {
            parts.Add(_text.Format("updated", FormatDate(activityAt)));
        }
    }

    private static string FormatDate(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString("yyyy-MM-dd");
    }

    private string ErrorTitle(GitHubErrorKind errorKind)
    {
        return errorKind switch
        {
            GitHubErrorKind.AuthenticationRequired => _text.Get("error_auth"),
            GitHubErrorKind.CommandFailed => _text.Get("error_command"),
            GitHubErrorKind.InvalidQuery => _text.Get("error_invalid_query"),
            GitHubErrorKind.InvalidResponse => _text.Get("error_invalid_response"),
            GitHubErrorKind.CliUnavailable => _text.Get("error_unavailable"),
            GitHubErrorKind.Timeout => _text.Get("error_timeout"),
            _ => _text.Get("error_command"),
        };
    }

    private string ErrorFallbackMessage(GitHubErrorKind errorKind)
    {
        return errorKind switch
        {
            GitHubErrorKind.AuthenticationRequired => _text.Get("fallback_auth"),
            GitHubErrorKind.CommandFailed => _text.Get("fallback_command"),
            GitHubErrorKind.InvalidQuery => _text.Get("fallback_invalid_query"),
            GitHubErrorKind.InvalidResponse => _text.Get("fallback_invalid_response"),
            GitHubErrorKind.CliUnavailable => _text.Get("fallback_unavailable"),
            GitHubErrorKind.Timeout => _text.Get("fallback_timeout"),
            _ => _text.Get("fallback_error"),
        };
    }

    private string EmptyTitle(QueryKind queryKind)
    {
        return queryKind switch
        {
            QueryKind.MyRepositories => _text.Get("empty_my_repos"),
            QueryKind.RepositorySearch => _text.Get("empty_repos"),
            QueryKind.PullRequests => _text.Get("empty_prs"),
            QueryKind.MyWork => _text.Get("empty_work"),
            QueryKind.Organizations => _text.Get("empty_orgs"),
            QueryKind.OrganizationRepositories => _text.Get("empty_org_repos"),
            QueryKind.Trend => _text.Get("empty_trend"),
            QueryKind.DirectRepository => _text.Get("empty_repo"),
            QueryKind.DirectPullRequest => _text.Get("empty_pr"),
            _ => _text.Get("empty_results"),
        };
    }

    private ErrorRecovery CreateRecovery(
        GitHubErrorKind errorKind,
        string retryQuery)
    {
        return errorKind switch
        {
            GitHubErrorKind.CliUnavailable => new ErrorRecovery(
                Hint: _text.Get("install_hint"),
                Url: "https://cli.github.com/",
                OpenTitle: Localization.Prefix + "install_page"),
            GitHubErrorKind.AuthenticationRequired => new ErrorRecovery(
                Hint: _text.Get("auth_hint"),
                Url: "https://cli.github.com/manual/gh_auth_login",
                OpenTitle: Localization.Prefix + "auth_usage"),
            GitHubErrorKind.InvalidQuery => new ErrorRecovery(
                Hint: _text.Get("invalid_hint"),
                QueryText: HomeQuery,
                QueryTitle: Localization.Prefix + "go_home"),
            _ => new ErrorRecovery(
                Hint: _text.Get("retry_hint"),
                QueryText: retryQuery,
                QueryTitle: Localization.Prefix + "retry_query",
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
