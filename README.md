# GitHub CLI for Flow Launcher

通过本机已登录的 [`gh`](https://cli.github.com/) 快速查询 GitHub。插件不保存 Token，也不直接接入 GitHub SDK。

## 功能

- 列出当前账号拥有的公开与私有仓库
- 搜索仓库及 Pull Request
- 汇总我创建的 PR 与等待我 Review 的 PR
- 列出当前账号所在组织并打开组织主页
- 查看近 1、7、30 天新建的热门仓库

## 查询语法

| 输入 | 作用 |
| --- | --- |
| `gh` | 显示功能入口 |
| `gh repo` | 列出自己的仓库 |
| `gh repo flow launcher` | 全局搜索仓库；`r` 是别名 |
| `gh org` | 列出自己所在的组织；`o` 是别名 |
| `gh pr owner/repo` | 列出指定仓库的开放 PR；`p` 是别名 |
| `gh pr review-requested:@me` | 跨仓库搜索 PR |
| `gh me` | 汇总我的开放 PR 和待 Review PR |
| `gh trend` | 近 7 天新建的热门仓库；`t` 是别名 |
| `gh trend daily python` | 近 1 天新建的 Python 热门仓库 |
| `gh trend monthly rust` | 近 30 天新建的 Rust 热门仓库 |
| `gh owner/repo#123` | 直接打开仓库或 PR |

`trend` 使用 `gh search repos --created ... --sort stars`，表示“时间窗口内新建仓库按当前 Star 总数排序”，不是 GitHub 官方 Trending 算法，也不代表窗口内新增 Star 数。

选中仓库、组织或 PR 后按 `Enter` 打开网页；按 `Shift+Enter` 可复制名称、URL，或切换到关联查询。`Ctrl+R` 绕过缓存重新查询。

## 环境要求

- Windows 10 或更高版本
- Flow Launcher 2.1.3 或更高版本
- GitHub CLI（已在 2.97.0 验证）
- 已通过 `gh auth login` 登录目标 GitHub 账号

插件直接继承 `gh` 的认证、权限、代理与 Host 配置。它不会调用 `gh auth token`，也不会显示凭据。

如果在 Flow Launcher 运行期间使用 `gh auth switch` 切换账号，请按 `Ctrl+R` 强制刷新或重启 Flow Launcher，避免继续看到最多缓存 5 分钟的旧账号结果。

## 构建

项目固定使用 .NET SDK 9.0.318。SDK 安装在仓库自己的 `.tools` 目录，不修改系统 PATH：

```powershell
.\scripts\bootstrap.ps1
.\scripts\build.ps1
```

构建会依次恢复锁定依赖、运行测试、发布插件，并生成：

```text
artifacts/plugin/
artifacts/Flow.Launcher.Plugin.GitHubCli.zip
```

## 本地安装

```powershell
.\scripts\install-local.ps1
```

随后在 Flow Launcher 中执行 `Restart Flow Launcher`。

## 发布

1. 同步修改 `plugin.json` 与 `packaging/*.json` 中的版本。
2. 推送 `v<version>` 标签；Release 工作流会测试并上传插件 ZIP。
3. 将 `packaging/GitHub CLI-47615338-321a-419e-b3c4-38ea9f11c614.json` 复制到 `Liaco123/Flow.Launcher.PluginsManifest` fork 的 `plugins` 目录。
4. 从 fork 向 `Flow-Launcher/Flow.Launcher.PluginsManifest` 的默认分支提交 Pull Request。

首次商店提交需要可公开访问的源代码、GitHub Release ZIP、图标 CDN 地址及自动构建/发布工作流。详见 [Flow Launcher PluginsManifest](https://github.com/Flow-Launcher/Flow.Launcher.PluginsManifest)。

## 开发基线

- `net9.0-windows`
- `Flow.Launcher.Plugin` 5.3.1
- `IAsyncPlugin`：异步执行并取消过期的 `gh` 子进程
- `IContextMenu`：提供复制与关联导航
- `ProcessStartInfo.ArgumentList`：不经过 shell 拼接用户输入

项目采用 MIT License。
