# Development and installation

[English README](../README.md) | [中文 README](../README.zh-CN.md)

## Build and verify

```powershell
.\scripts\bootstrap.ps1
.\scripts\build.ps1
```

The repository-local .NET SDK 9.0.318 lives under `.tools` and does not change the system PATH. The build restores locked dependencies, runs the C# tests, publishes Release files, runs installer regression checks, creates the ZIP, and validates its contents.

CI and Release run the same installer and package checks. To run them against existing artifacts:

```powershell
.\scripts\test-install-local.ps1
.\scripts\test-package.ps1
```

The plugin targets `net9.0-windows` with `Flow.Launcher.Plugin` 5.3.1. The API package is excluded from the plugin's runtime output because Flow supplies it. The test project directly references the same version to supply the API while running outside Flow. `IAsyncPlugin` cancels obsolete `gh` subprocesses; `IContextMenu` provides copying and related navigation. Arguments use `ProcessStartInfo.ArgumentList` without shell interpolation.

## ZIP layout

`artifacts/Flow.Launcher.Plugin.GitHubCli.zip` contains the contents of `artifacts/plugin` directly, without a wrapping folder:

```text
plugin.json
Flow.Launcher.Plugin.GitHubCli.dll
Flow.Launcher.Plugin.GitHubCli.deps.json
Images/
    app.png
Languages/
    en.xaml
    zh-cn.xaml
    zh-tw.xaml
```

Additional plugin dependencies may appear beside the entry DLL. `Flow.Launcher.Plugin.dll`, PDBs, and XML documentation are excluded. The package check verifies the root manifest, ID/version, entry DLL, icon, unique entry paths, and hashes against the published files.

## Localization

Flow loads the `Languages` XAML dictionaries, with English as the default and missing-language fallback. UI strings use `IPublicAPI.GetTranslation` with the `flowlauncher_plugin_githubcli_` prefix. Menu, context, status, error, and cache labels are looked up when rendering results, including labels for cached GitHub data. Internal error messages retain resource keys and format arguments until display; external CLI errors and GitHub content remain in their original language.

`IPluginI18n` provides the localized plugin description in Flow's plugin list. No separate language setting is stored. Change Flow's language and query again to see the updated text. Add a language dictionary with the same keys and placeholders as `en.xaml` to extend translations; the tests check key and format-parameter parity, and the package check verifies that all three supported dictionaries are included.

Download the ZIP and run `pm install <path to zip>` inside Flow. This installation flow follows [Google Preview](https://github.com/utkarshalpha/Flow.Launcher.Plugin.GooglePreview); its [project file](https://github.com/utkarshalpha/Flow.Launcher.Plugin.GooglePreview/blob/main/Flow.Launcher.Plugin.GooglePreview.csproj) also excludes the host API DLL from runtime output.

## Local and portable installs

```powershell
.\scripts\install-local.ps1 -SkipBuild
```

The default destination is `%APPDATA%\FlowLauncher\Plugins\GitHubCli-<version>`. The installer checks ID/version, DLL, and icon against the source manifest. It finds existing copies by manifest ID, including folders named by Flow's ZIP installer, and moves them into `PluginBackups` beside the selected `Plugins` directory. It stages the new files before replacing the old copies; if a move fails, it restores the copies already moved. Other plugins and settings are preserved.

Preview without changing installed files:

```powershell
.\scripts\install-local.ps1 -SkipBuild -WhatIf
```

Portable installs use the active `UserData\Plugins` directory. Supply that directory's actual path with `-PluginsRoot`:

```powershell
.\scripts\install-local.ps1 -SkipBuild -PluginsRoot '<actual UserData\Plugins path>'
```

Restart Flow after installation. For manual copying, put the published files into one subfolder of the active `Plugins` directory. Place the manifest, DLL, and `Images` directly inside it; move previous copies outside `Plugins`.

## Installation troubleshooting

Flow 2.1.3/2.1.4 skips all copies when multiple folders have the same plugin ID and the same highest version. This follows the [v2.1.4 PluginConfig implementation](https://github.com/Flow-Launcher/Flow.Launcher/blob/v2.1.4/Flow.Launcher.Core/Plugin/PluginConfig.cs).

1. Run `.\scripts\install-local.ps1` to build, back up every matching copy, and install one copy.
2. Run `Restart Flow Launcher`; check **GitHub CLI** under Settings → Plugins or type `gh`.
3. If it is still missing, search the latest log under `%APPDATA%\FlowLauncher\Logs` (portable: `UserData\Logs`) for `GitHub CLI`, `GitHubCli`, or `47615338-321a-419e-b3c4-38ea9f11c614`.

Backups must stay outside `Plugins`, whose immediate subfolders Flow scans for plugins. The active data directory follows Flow's [DataLocation implementation](https://github.com/Flow-Launcher/Flow.Launcher/blob/v2.1.4/Flow.Launcher.Infrastructure/UserSettings/DataLocation.cs).

## Publishing

1. Update the version in `src/Flow.Launcher.Plugin.GitHubCli/plugin.json` and `packaging/GitHub CLI-47615338-321a-419e-b3c4-38ea9f11c614.json`.
2. Push a matching `v<version>` tag; the Release workflow verifies the version, tests, builds, and uploads the ZIP.
3. For the initial store submission, copy the packaging manifest into the `plugins` directory of the `Liaco123/Flow.Launcher.PluginsManifest` fork.
4. Submit a pull request to `Flow-Launcher/Flow.Launcher.PluginsManifest`, updating the existing submission when applicable.

The first submission requires public source code, a GitHub Release ZIP, a CDN icon URL, and automated build/release workflows. See [PluginsManifest](https://github.com/Flow-Launcher/Flow.Launcher.PluginsManifest) for the current requirements. The release asset filename stays `Flow.Launcher.Plugin.GitHubCli.zip` to match the packaging manifest's download URL.
