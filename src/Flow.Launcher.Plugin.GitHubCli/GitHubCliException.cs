namespace Flow.Launcher.Plugin.GitHubCli;

internal enum GitHubCliFailureKind
{
    NotFound,
    TimedOut,
    CommandFailed,
}
internal sealed class GitHubCliException : Exception
{
    internal GitHubCliException(
        GitHubCliFailureKind kind,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
    }

    internal GitHubCliFailureKind Kind { get; }
}
