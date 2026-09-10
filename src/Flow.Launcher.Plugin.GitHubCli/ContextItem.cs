namespace Flow.Launcher.Plugin.GitHubCli;

internal sealed record ContextItem(
    string? Url = null,
    string? OpenTitle = null,
    string? Identifier = null,
    string? QueryText = null,
    string? QueryTitle = null,
    bool ForceRequery = false);
