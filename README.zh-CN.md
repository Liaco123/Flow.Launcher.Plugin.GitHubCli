# GitHub CLI for Flow Launcher

[English](README.md) | 简体中文

通过本机已登录的 [`gh`](https://cli.github.com/)，在 [Flow Launcher](https://www.flowlauncher.com) 中查询 GitHub。

- 列出当前账号拥有的公开与私有仓库。
- 搜索仓库和 Pull Request。
- 查看我创建的 PR 与等待我 Review 的 PR。
- 浏览当前账号所在的组织及其仓库。
- 查看近 1、7、30 天新建的热门仓库。

## 安装

1. 安装 [GitHub CLI](https://cli.github.com/)，执行 `gh auth login` 登录。
2. 从 [Releases](https://github.com/Liaco123/Flow.Launcher.Plugin.GitHubCli/releases) 下载 `Flow.Launcher.Plugin.GitHubCli.zip`。
3. 在 Flow Launcher 中执行下列命令，将 `<path to zip>` 替换为下载的 ZIP 完整路径。

```text
pm install <path to zip>
```

确认 Flow 的安装提示。若未自动重启，执行 `Restart Flow Launcher`。

需要 Windows 10 或更高版本、Flow Launcher 2.1.3 或更高版本，以及 PATH 中可用的 GitHub CLI（已在 2.97.0 验证）。.NET 运行时和插件接口 DLL 由 Flow 提供。

[插件商店提交](https://github.com/Flow-Launcher/Flow.Launcher.PluginsManifest/pull/753)仍待审核。上架后可通过名称安装：

```text
pm install GitHub CLI
```

## 使用

按 Flow 快捷键（默认 `Alt+Space`），输入 `gh` 打开功能菜单。

| 输入 | 作用 |
| --- | --- |
| `gh` | 显示功能入口 |
| `gh repo` | 列出自己的仓库 |
| `gh repo flow launcher` | 全局搜索仓库；`r` 是别名 |
| `gh org` | 列出自己所在的组织；`o` 是别名 |
| `gh org <organization> [filter]` | 列出指定组织下可访问的仓库，可选按名称或描述筛选 |
| `gh pr owner/repo` | 列出指定仓库的开放 PR；`p` 是别名 |
| `gh pr review-requested:@me` | 跨仓库搜索 PR |
| `gh me` | 汇总我的开放 PR 和待 Review PR |
| `gh trend` | 近 7 天新建的热门仓库；`t` 是别名 |
| `gh trend daily python` | 近 1 天新建的 Python 热门仓库 |
| `gh trend monthly rust` | 近 30 天新建的 Rust 热门仓库 |
| `gh owner/repo#123` | 直接打开仓库或 PR |

按 `Enter` 在浏览器打开结果。`Shift+Enter` 上下文菜单可浏览组织仓库、复制名称或 URL、切换到关联查询。`Ctrl+R` 绕过缓存重新查询。

## 说明

- 插件继承 `gh` 的认证、权限、代理与 Host 配置，不保存 Token，不调用 `gh auth token`，也不直接接入 GitHub SDK。
- 使用 `gh auth switch` 切换账号后，按 `Ctrl+R` 或重启 Flow，避免继续显示最多缓存 5 分钟的旧账号结果。
- `trend` 使用 `gh search repos --created ... --sort stars`，按当前 Star 总数排列窗口内新建的仓库，不代表窗口内新增 Star 数，也不是 GitHub 官方 Trending 算法。
- 插件跟随 Flow 的界面语言，支持英文、简体中文和繁体中文，其他语言回退英文。在 Flow 设置中切换语言后，重新查询即可应用；查询关键字保持不变。
- 只保留一份安装。Flow 2.1.3/2.1.4 会跳过同一 ID、相同最高版本的全部副本。插件未出现在列表时，参考[安装排查](docs/DEVELOPMENT.md#installation-troubleshooting)。

## 构建

在仓库根目录运行：

```powershell
.\scripts\bootstrap.ps1
.\scripts\build.ps1
```

脚本使用仓库本地的 .NET SDK 9.0.318，恢复锁定依赖并运行测试。可安装 ZIP 位于 `artifacts/Flow.Launcher.Plugin.GitHubCli.zip`，发布文件位于 `artifacts/plugin`。

通过 `pm install <path to zip>` 安装构建出的 ZIP，或使用本地安装脚本备份旧副本、只安装一份：

```powershell
.\scripts\install-local.ps1 -SkipBuild
```

本地安装后重启 Flow。便携版路径、ZIP 结构、验证和发布步骤见[开发与安装详情](docs/DEVELOPMENT.md)。

README 与 ZIP 安装结构参考 [Google Preview](https://github.com/utkarshalpha/Flow.Launcher.Plugin.GooglePreview)。

## 许可证

[MIT](LICENSE)
