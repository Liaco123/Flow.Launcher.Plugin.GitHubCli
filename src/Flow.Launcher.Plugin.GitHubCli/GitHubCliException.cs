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
        Exception? innerException = null,
        params object[] messageArguments)
        : base(message, innerException)
    {
        Kind = kind;
        MessageArguments = messageArguments;
    }

    internal GitHubCliFailureKind Kind { get; }

    internal object[] MessageArguments { get; }
}
