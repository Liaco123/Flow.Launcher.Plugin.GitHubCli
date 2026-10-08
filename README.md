# GitHub CLI for Flow Launcher

English | [简体中文](README.zh-CN.md)

Search GitHub in [Flow Launcher](https://www.flowlauncher.com) using your locally authenticated [`gh`](https://cli.github.com/) CLI.

- List your public and private repositories.
- Search repositories and pull requests.
- View pull requests you authored and those awaiting your review.
- Browse your organizations and their repositories.
- Explore popular repositories created in the last 1, 7, or 30 days.

## Install

1. Install [GitHub CLI](https://cli.github.com/) and sign in with `gh auth login`.
2. Download `Flow.Launcher.Plugin.GitHubCli.zip` from [Releases](https://github.com/Liaco123/Flow.Launcher.Plugin.GitHubCli/releases).
3. In Flow Launcher, run the command below, replacing `<path to zip>` with the downloaded ZIP's full path.

```text
pm install <path to zip>
```

Accept Flow's installation prompt. If Flow does not restart automatically, run `Restart Flow Launcher`.

Requires Windows 10 or later, Flow Launcher 2.1.3 or later, and GitHub CLI on PATH (tested with 2.97.0). Flow provides the .NET runtime and plugin API DLL.

The [plugin store submission](https://github.com/Flow-Launcher/Flow.Launcher.PluginsManifest/pull/753) is awaiting review. Once the plugin is listed, you can install it with:

```text
pm install GitHub CLI
```

## Usage

Press your Flow hotkey (default `Alt+Space`) and type `gh` to open the feature menu.

| Input | Action |
| --- | --- |
| `gh` | Show the feature menu |
| `gh repo` | List your repositories |
| `gh repo flow launcher` | Search repositories globally; `r` is an alias |
| `gh org` | List your organizations; `o` is an alias |
| `gh org <organization> [filter]` | List accessible repositories in an organization, optionally filtered by name or description |
| `gh pr owner/repo` | List open pull requests in a repository; `p` is an alias |
| `gh pr review-requested:@me` | Search pull requests across repositories |
| `gh me` | Show your authored and review-requested open pull requests |
| `gh trend` | Show popular repositories created in the last 7 days; `t` is an alias |
| `gh trend daily python` | Show popular Python repositories created in the last day |
| `gh trend monthly rust` | Show popular Rust repositories created in the last 30 days |
| `gh owner/repo#123` | Open a repository or pull request directly |

Press `Enter` to open a result in your browser. The `Shift+Enter` context menu lets you browse an organization's repositories, copy names or URLs, or navigate to related queries. Press `Ctrl+R` to bypass the cache.

## Notes

- The plugin inherits `gh` authentication, permissions, proxy, and host configuration. It does not store tokens, call `gh auth token`, or use a GitHub SDK.
- After switching accounts with `gh auth switch`, press `Ctrl+R` or restart Flow to clear results cached for up to five minutes.
- `trend` uses `gh search repos --created ... --sort stars`: repositories created in the selected window, ranked by current total stars. It does not measure stars gained in that window or implement GitHub's official Trending algorithm.
- The plugin follows Flow's display language: English, Simplified Chinese, and Traditional Chinese are supported; other languages fall back to English. Change the language in Flow's settings and query again. Query keywords stay the same.
- Keep one installed copy. Flow 2.1.3/2.1.4 skips every copy if several folders share the same plugin ID and highest version. See [installation troubleshooting](docs/DEVELOPMENT.md#installation-troubleshooting) if the plugin disappears from the list.

## Build

From the repository root:

```powershell
.\scripts\bootstrap.ps1
.\scripts\build.ps1
```

The scripts use repository-local .NET SDK 9.0.318, restore locked dependencies, and run the tests. The installable package is `artifacts/Flow.Launcher.Plugin.GitHubCli.zip`; the published files are in `artifacts/plugin`.

Install the built ZIP with `pm install <path to zip>`, or use the local installer to back up previous copies and install one copy:

```powershell
.\scripts\install-local.ps1 -SkipBuild
```

Restart Flow after a local installation. See [development and installation details](docs/DEVELOPMENT.md) for portable installs, ZIP layout, verification, and publishing.

README and ZIP installation structure are adapted from [Google Preview](https://github.com/utkarshalpha/Flow.Launcher.Plugin.GooglePreview).

## License

[MIT](LICENSE)
