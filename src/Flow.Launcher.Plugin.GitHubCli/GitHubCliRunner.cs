using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Flow.Launcher.Plugin.GitHubCli;

internal interface IGitHubCliRunner
{
    Task<GhCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}

internal sealed record GhCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal sealed class GitHubCliRunner : IGitHubCliRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TerminationTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RedactionTimeout = TimeSpan.FromMilliseconds(50);

    private static readonly Regex SecretAssignmentPattern = new(
        @"\b(?<name>GH_TOKEN|GITHUB_TOKEN|GH_ENTERPRISE_TOKEN|GITHUB_ENTERPRISE_TOKEN)\s*=\s*(?:""[^""]*""|'[^']*'|[^\s]+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RedactionTimeout);

    private static readonly Regex AuthorizationPattern = new(
        @"\b(?<prefix>Authorization\s*:\s*(?:Bearer|Token|Basic)\s+)[^\s,;]+",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RedactionTimeout);

    private static readonly Regex UrlUserInfoPattern = new(
        @"(?<scheme>https?://)[^/\s@]+@",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RedactionTimeout);

    private static readonly Regex GitHubTokenPattern = new(
        @"(?<![A-Za-z0-9_])(?:github_pat_|gh[pousr]_)[A-Za-z0-9_]{8,}",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        RedactionTimeout);

    private readonly string _executablePath;
    private readonly TimeSpan _timeout;

    internal GitHubCliRunner(
        string executablePath = "gh",
        TimeSpan? timeout = null)
    {
        _executablePath = executablePath;
        _timeout = timeout ?? DefaultTimeout;
    }

    public async Task<GhCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        using var process = new Process
        {
            StartInfo = CreateStartInfo(arguments),
        };

        try
        {
            if (!process.Start())
            {
                throw new GitHubCliException(
                    GitHubCliFailureKind.NotFound,
                    "无法启动 GitHub CLI。");
            }
        }
        catch (Win32Exception exception)
        {
            throw new GitHubCliException(
                GitHubCliFailureKind.NotFound,
                "未找到 gh.exe。请先安装 GitHub CLI，并确保 gh 位于 PATH 中。",
                exception);
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        using var timeoutSource = new CancellationTokenSource(_timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);

        try
        {
            await process.WaitForExitAsync(linkedSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (
            timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            await AwaitTerminationAfterKillAsync(
                    process,
                    standardOutputTask,
                    standardErrorTask)
                .ConfigureAwait(false);
            throw new GitHubCliException(
                GitHubCliFailureKind.TimedOut,
                $"GitHub CLI 查询超过 {_timeout.TotalSeconds:0.#} 秒，已取消。");
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            await AwaitTerminationAfterKillAsync(
                    process,
                    standardOutputTask,
                    standardErrorTask)
                .ConfigureAwait(false);
            throw;
        }

        var standardOutput = await standardOutputTask.ConfigureAwait(false);
        var standardError = await standardErrorTask.ConfigureAwait(false);

        return new GhCommandResult(
            process.ExitCode,
            standardOutput,
            SanitizeError(standardError));
    }

    private ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["GH_PROMPT_DISABLED"] = "1";
        startInfo.Environment["GH_NO_UPDATE_NOTIFIER"] = "1";
        startInfo.Environment["GH_SPINNER_DISABLED"] = "1";
        startInfo.Environment["GH_TELEMETRY"] = "false";
        startInfo.Environment["NO_COLOR"] = "1";
        startInfo.Environment["CLICOLOR"] = "0";
        startInfo.Environment.Remove("GH_DEBUG");
        startInfo.Environment.Remove("GH_FORCE_TTY");
        startInfo.Environment.Remove("DEBUG");
        startInfo.Environment.Remove("CLICOLOR_FORCE");

        return startInfo;
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // The process exited, cannot be killed on this platform, or access was denied.
        }
    }

    private static async Task AwaitTerminationAfterKillAsync(
        Process process,
        Task<string> standardOutputTask,
        Task<string> standardErrorTask)
    {
        using var terminationSource = new CancellationTokenSource(TerminationTimeout);

        try
        {
            await process.WaitForExitAsync(terminationSource.Token).ConfigureAwait(false);
            await Task.WhenAll(standardOutputTask, standardErrorTask)
                .WaitAsync(terminationSource.Token)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or IOException
                or OperationCanceledException
                or ObjectDisposedException)
        {
            // Cancellation must remain bounded even if the process or redirected streams stall.
        }

        ObserveFault(standardOutputTask);
        ObserveFault(standardErrorTask);
    }

    private static void ObserveFault(Task task)
    {
        _ = task.ContinueWith(
            static completedTask => _ = completedTask.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    internal static string SanitizeError(string value)
    {
        const string redacted = "[REDACTED]";
        const int maximumLength = 500;
        const int maximumInspectionLength = 2_048;
        var builder = new StringBuilder(Math.Min(value.Length, maximumInspectionLength));

        foreach (var character in value)
        {
            if (character is '\r' or '\n' or '\t' || !char.IsControl(character))
            {
                builder.Append(character);
            }

            if (builder.Length == maximumInspectionLength)
            {
                break;
            }
        }

        var sanitized = builder.ToString();
        sanitized = SecretAssignmentPattern.Replace(
            sanitized,
            match => $"{match.Groups["name"].Value}={redacted}");
        sanitized = AuthorizationPattern.Replace(
            sanitized,
            match => $"{match.Groups["prefix"].Value}{redacted}");
        sanitized = UrlUserInfoPattern.Replace(
            sanitized,
            match => $"{match.Groups["scheme"].Value}{redacted}@");
        sanitized = GitHubTokenPattern.Replace(sanitized, redacted);

        sanitized = sanitized.Trim();
        return sanitized.Length <= maximumLength
            ? sanitized
            : sanitized[..maximumLength];
    }
}
