namespace Flow.Launcher.Plugin.GitHubCli;

internal enum QueryKind
{
    Home,
    MyRepositories,
    RepositorySearch,
    PullRequests,
    MyWork,
    Organizations,
    Trend,
    DirectRepository,
    DirectPullRequest,
    Invalid,
}

internal enum TrendPeriod
{
    Daily = 1,
    Weekly = 7,
    Monthly = 30,
}

internal sealed record ParsedQuery(
    QueryKind Kind,
    string SearchText = "",
    string? Repository = null,
    int? PullRequestNumber = null,
    string? Organization = null,
    TrendPeriod? TrendPeriod = null,
    string? Language = null,
    string? ErrorMessage = null);

internal enum GitHubItemKind
{
    Repository,
    PullRequest,
    Organization,
}

internal enum GitHubRelationship
{
    None,
    Authored,
    ReviewRequested,
}

internal enum GitHubErrorKind
{
    AuthenticationRequired,
    CommandFailed,
    InvalidQuery,
    InvalidResponse,
    CliUnavailable,
    Timeout,
}

internal sealed record GitHubItem(
    GitHubItemKind Kind,
    string Title,
    string Repository,
    string Url,
    string? Description = null,
    int? Number = null,
    string? State = null,
    bool IsDraft = false,
    string? Author = null,
    GitHubRelationship Relationship = GitHubRelationship.None,
    int? StarCount = null,
    int? ForkCount = null,
    string? Language = null,
    string? Visibility = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? UpdatedAt = null,
    DateTimeOffset? PushedAt = null,
    string? ViewerPermission = null,
    bool IsArchived = false,
    bool IsFork = false,
    string? ReviewDecision = null);

internal sealed record GitHubQueryResult(
    IReadOnlyList<GitHubItem> Items,
    GitHubErrorKind? ErrorKind = null,
    string? ErrorMessage = null,
    bool IsFromCache = false,
    bool IsStale = false)
{
    internal bool IsSuccess => ErrorKind is null;
}
