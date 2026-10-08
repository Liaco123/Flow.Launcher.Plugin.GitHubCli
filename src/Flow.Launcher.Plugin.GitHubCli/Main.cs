using Flow.Launcher.Plugin;

namespace Flow.Launcher.Plugin.GitHubCli;

/// <summary>
/// Flow Launcher entry point for querying GitHub through the local GitHub CLI.
/// </summary>
public sealed class Main : IAsyncPlugin, IContextMenu, IPluginI18n
{
    private GitHubService _gitHubService = null!;
    private ResultFactory _resultFactory = null!;
    private Localization? _localization;

    /// <inheritdoc />
    public Task InitAsync(PluginInitContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _gitHubService = new GitHubService(new GitHubCliRunner());
        _resultFactory = new ResultFactory(context.API);
        _localization = new Localization(context.API);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public string GetTranslatedPluginTitle() =>
        _localization?.Get("plugin_name") ?? "GitHub CLI";

    /// <inheritdoc />
    public string GetTranslatedPluginDescription() =>
        _localization?.Get("plugin_description") ??
        "Search your repositories, organizations, pull requests, and recent popular repositories using the local gh CLI";

    /// <inheritdoc />
    public async Task<List<Result>> QueryAsync(
        Query query,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(query);

        var parsedQuery = QueryParser.Parse(query.Search);

        if (parsedQuery.Kind == QueryKind.Home)
        {
            return _resultFactory.CreateHomeResults();
        }

        if (parsedQuery.Kind == QueryKind.Invalid)
        {
            return _resultFactory.CreateInvalidQueryResult(parsedQuery);
        }

        var retryQuery = string.IsNullOrWhiteSpace(query.TrimmedQuery)
            ? "gh "
            : query.TrimmedQuery;

        try
        {
            var queryResult = await _gitHubService.ExecuteAsync(
                parsedQuery,
                query.IsReQuery,
                token).ConfigureAwait(false);

            return _resultFactory.CreateQueryResults(
                parsedQuery,
                queryResult,
                retryQuery);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return _resultFactory.CreateUnexpectedErrorResult(retryQuery);
        }
    }

    /// <inheritdoc />
    public List<Result> LoadContextMenus(Result selectedResult)
    {
        return _resultFactory.CreateContextMenus(selectedResult);
    }
}
