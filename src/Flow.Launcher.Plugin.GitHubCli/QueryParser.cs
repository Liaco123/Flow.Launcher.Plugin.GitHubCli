using System.Globalization;
using System.Text.RegularExpressions;

namespace Flow.Launcher.Plugin.GitHubCli;

internal static class QueryParser
{
    private static readonly char[] CommandSeparators = [' ', '\t', '\r', '\n'];

    private static readonly Regex DirectTargetPattern = new(
        "^(?<repository>[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+)(?:#(?<number>.*))?$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    internal static ParsedQuery Parse(string? input)
    {
        var value = input?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return new ParsedQuery(QueryKind.Home);
        }

        var directTarget = ParseDirectTarget(value);
        if (directTarget is not null)
        {
            return directTarget;
        }

        var (command, remainder) = SplitHead(value);
        return command.ToLowerInvariant() switch
        {
            "home" => remainder.Length == 0
                ? new ParsedQuery(QueryKind.Home)
                : Invalid("home 命令不接受参数。"),
            "repo" or "repos" or "r" => remainder.Length == 0
                ? new ParsedQuery(QueryKind.MyRepositories)
                : new ParsedQuery(QueryKind.RepositorySearch, SearchText: remainder),
            "pr" or "prs" or "p" => ParsePullRequests(remainder),
            "me" or "mine" or "m" => new ParsedQuery(QueryKind.MyWork, SearchText: remainder),
            "org" or "o" => ParseOrganizations(remainder),
            "trend" or "trending" or "t" => ParseTrend(remainder),
            _ => new ParsedQuery(QueryKind.RepositorySearch, SearchText: value),
        };
    }

    private static ParsedQuery ParsePullRequests(string remainder)
    {
        if (remainder.Length == 0)
        {
            return new ParsedQuery(QueryKind.PullRequests);
        }

        var directTarget = ParseDirectTarget(remainder);
        if (directTarget?.Kind == QueryKind.DirectPullRequest)
        {
            return directTarget;
        }

        var (first, searchText) = SplitHead(remainder);
        if (IsRepository(first))
        {
            return new ParsedQuery(
                QueryKind.PullRequests,
                SearchText: searchText,
                Repository: first);
        }

        if (first.Contains('#', StringComparison.Ordinal))
        {
            return Invalid("PR 直达格式应为 owner/repo#编号，且编号必须大于 0。");
        }

        return new ParsedQuery(QueryKind.PullRequests, SearchText: remainder);
    }

    private static ParsedQuery ParseOrganizations(string remainder)
    {
        if (remainder.Length == 0)
        {
            return new ParsedQuery(QueryKind.Organizations);
        }

        var (organization, searchText) = SplitHead(remainder);
        if (!IsOwner(organization))
        {
            return Invalid("组织名只能包含字母、数字、点、下划线或连字符。");
        }

        return new ParsedQuery(
            QueryKind.OrganizationRepositories,
            SearchText: searchText,
            Organization: organization);
    }

    private static ParsedQuery ParseTrend(string remainder)
    {
        if (remainder.Length == 0)
        {
            return new ParsedQuery(
                QueryKind.Trend,
                TrendPeriod: Flow.Launcher.Plugin.GitHubCli.TrendPeriod.Weekly);
        }

        var (periodText, trailingLanguage) = SplitHead(remainder);
        var period = periodText.ToLowerInvariant() switch
        {
            "daily" or "day" or "1" or "1d" or "d" => Flow.Launcher.Plugin.GitHubCli.TrendPeriod.Daily,
            "weekly" or "week" or "7" or "7d" or "w" => Flow.Launcher.Plugin.GitHubCli.TrendPeriod.Weekly,
            "monthly" or "month" or "30" or "30d" => Flow.Launcher.Plugin.GitHubCli.TrendPeriod.Monthly,
            _ => (Flow.Launcher.Plugin.GitHubCli.TrendPeriod?)null,
        };

        string languageText;
        if (period is null)
        {
            if (periodText.All(char.IsAsciiDigit)
                || (periodText.EndsWith('d')
                    && periodText[..^1].All(char.IsAsciiDigit)))
            {
                return Invalid("Trend 周期只能是 daily、weekly 或 monthly。");
            }

            period = Flow.Launcher.Plugin.GitHubCli.TrendPeriod.Weekly;
            languageText = remainder;
        }
        else
        {
            languageText = trailingLanguage;
        }

        var language = NormalizeLanguage(languageText);
        if (languageText.Length > 0 && language is null)
        {
            return Invalid("语言参数不能为空。");
        }

        return new ParsedQuery(
            QueryKind.Trend,
            TrendPeriod: period,
            Language: language);
    }

    private static ParsedQuery? ParseDirectTarget(string value)
    {
        var match = DirectTargetPattern.Match(value);
        if (!match.Success)
        {
            return null;
        }

        var repository = match.Groups["repository"].Value;
        var numberGroup = match.Groups["number"];
        if (!numberGroup.Success)
        {
            return new ParsedQuery(
                QueryKind.DirectRepository,
                Repository: repository);
        }

        if (!int.TryParse(
                numberGroup.Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var number)
            || number <= 0)
        {
            return Invalid("PR 编号必须是大于 0 的整数。");
        }

        return new ParsedQuery(
            QueryKind.DirectPullRequest,
            Repository: repository,
            PullRequestNumber: number);
    }

    private static string? NormalizeLanguage(string value)
    {
        if (value.Length == 0)
        {
            return null;
        }

        const string languagePrefix = "language:";
        const string shortPrefix = "lang:";

        var language = value.StartsWith(languagePrefix, StringComparison.OrdinalIgnoreCase)
            ? value[languagePrefix.Length..].Trim()
            : value.StartsWith(shortPrefix, StringComparison.OrdinalIgnoreCase)
                ? value[shortPrefix.Length..].Trim()
                : value;

        return language.Length == 0 ? null : language;
    }

    private static (string Head, string Tail) SplitHead(string value)
    {
        var separatorIndex = value.IndexOfAny(CommandSeparators);
        if (separatorIndex < 0)
        {
            return (value, string.Empty);
        }

        return (
            value[..separatorIndex],
            value[(separatorIndex + 1)..].Trim());
    }

    private static bool IsRepository(string value)
    {
        var slashIndex = value.IndexOf('/');
        return slashIndex > 0
            && slashIndex == value.LastIndexOf('/')
            && IsOwner(value[..slashIndex])
            && IsRepositoryName(value[(slashIndex + 1)..]);
    }

    private static bool IsOwner(string value) =>
        value.Length > 0 && value.All(IsRepositoryCharacter);

    private static bool IsRepositoryName(string value) =>
        value.Length > 0 && value.All(IsRepositoryCharacter);

    private static bool IsRepositoryCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value) || value is '_' or '-' or '.';

    private static ParsedQuery Invalid(string message) =>
        new(QueryKind.Invalid, ErrorMessage: message);
}
