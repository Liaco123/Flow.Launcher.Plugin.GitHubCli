using System.Globalization;
using Flow.Launcher.Plugin;

namespace Flow.Launcher.Plugin.GitHubCli;

internal sealed class Localization(IPublicAPI api)
{
    internal const string Prefix = "flowlauncher_plugin_githubcli_";

    internal string Get(string key) => api.GetTranslation(Prefix + key);

    internal string Format(string key, params object[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), arguments);

    internal string? Message(string? message, object[]? arguments = null) =>
        message is not null && message.StartsWith(Prefix, StringComparison.Ordinal)
            ? Format(message[Prefix.Length..], arguments ?? [])
            : message;

    internal string Status(string value) => value.ToUpperInvariant() switch
    {
        "OPEN" => Get("status_open"),
        "CLOSED" => Get("status_closed"),
        "MERGED" => Get("status_merged"),
        "PUBLIC" => Get("status_public"),
        "PRIVATE" => Get("status_private"),
        "INTERNAL" => Get("status_internal"),
        "APPROVED" => Get("status_approved"),
        "CHANGES_REQUESTED" => Get("status_changes_requested"),
        "REVIEW_REQUIRED" => Get("status_review_required"),
        _ => value,
    };
}
