# BabyKeyboard — Agent 规则手册

给幼儿随意敲键盘的 Windows 全屏互动软件。C# / .NET 10 / WPF，Windows 11 x64，无第三方 NuGet 依赖。

- 公开仓库：https://github.com/Frank-jpeg/baby-keyboard （账号 `Frank-jpeg`，默认分支 `main`）
- 面向使用者/访客的说明在 `README.md`；验证证据在 `docs/`。**本文件只写"改代码时必须知道"的规则。**

## 构建与测试

本机 SDK（不要在项目里再装一份）：`../tools/dotnet10/dotnet.exe`（.NET SDK 10.0.400）

```bash
cd <项目根>
DOTNET="../tools/dotnet10/dotnet.exe"
"$DOTNET" build BabyKeyboard.slnx -v q --nologo          # ~7 秒
"$DOTNET" build tests/BabyKeyboard.Tests/BabyKeyboard.Tests.csproj -c Release
tests/BabyKeyboard.Tests/bin/Release/net10.0-windows/BabyKeyboard.Tests.exe   # 核心测试
```

- ⚠️ **`build.ps1` 在 Agent 沙箱里跑不动**（PowerShell 沙箱不可用；Bash 里调 `powershell.exe` 也被安全策略拒）。
  出包时**手动按 `build.ps1` 的顺序逐条敲**：build 测试工程 → 跑 Tests.exe →
  `dotnet publish src/BabyKeyboard.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o <out>` →
  拷成 `宝宝键盘-v<版本>.exe` → 拷 `docs/使用说明.txt` → `Compress-Archive`。
- ⛔ **不要跑 `build.ps1 -NativeTests` / `Tests.exe --integration`**：会**接管真实键鼠并全屏**，
  在 Agent 沙箱里跑有把用户电脑锁死的风险。这一步只能由用户在自己的终端里跑。
- `--render-preview <png>` 会短暂起一个隐藏的全屏 WPF 窗口，中等风险，需要时单独确认。

## 红线

- **`TreatWarningsAsErrors = true`**（`Directory.Build.props`）。警告即编译失败——不要靠 `#pragma` 绕，
  找到根因。构建输出必须是 0 警告 0 错误。
- **输入钩子线程里不许做任何阻塞操作**：不碰磁盘、不做音频/绘图、不调 UI、不拿锁。
  `src/BabyKeyboard.App/InputInterceptor.cs` 的 LL 钩子回调一旦超过系统 300ms 上限会被静默摘掉，键直接漏出去。
- **`src/BabyKeyboard.Core` 不依赖界面框架**，只用单调时钟。改 Core 不许引入 WPF/System.Windows 依赖。
- 改动后 `git status` 必须仍然 clean（`artifacts/`、`**/bin|obj/`、`.packages/` 已被 `.gitignore` 覆盖，
  不要把它们加进索引）。

## 版本与发布

- 版本号唯一来源：`src/BabyKeyboard.App/BabyKeyboard.App.csproj` 的 `<Version>`。
  改它 → `build.ps1` 会在 `artifacts/release/v<版本>/` 下建新目录（旧版保留）。
- **`docs/使用说明.txt` 是发布包内说明的唯一来源**（`build.ps1` 从 `docs/` 取第一个 `*.txt` 拷进包）。
  改版本号时**三处一起改**：csproj、`docs/使用说明.txt` 首行标题、README 的版本历史段与产物路径。
  > 教训：2026-09-26 接手时发现 README 正文已写 v1.1.3，但标题、exe 名、release 路径还留 v1.1.1/v1.1.2。
- 发布包 = exe + 使用说明.txt + preview.png + sound-preview.wav + RUNTIME-NOTICES.txt 的 zip。

## 结构

| 路径 | 职责 |
|---|---|
| `src/BabyKeyboard.Core` | 解锁状态机、键帽名称、离线音色合成（无 UI 依赖） |
| `src/BabyKeyboard.App` | 全屏窗口、每显示器 DPI 布局、Win32 键鼠钩子、看护进程、PCM 播放与动画 |
| `tests/BabyKeyboard.Tests` | 核心断言 + Windows 原生集成测试 |
| `docs/plans/` | 每个版本的实现计划（按 `YYYY-MM-DD-vX.Y.Z-主题.md`） |
| `docs/VERIFICATION*.md` | 版本验证记录 |

## 协作

- **改完就推**：`git add -A` → 扫描暂存区 → commit → `push origin main` → 比对 SHA。
  不问、不攒。命令与踩坑见技能 `github-private-push`。
- 跨逻辑主题的改动拆成多个 commit。
