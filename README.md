# ALLS Bootstrapper

ALLS Bootstrapper 是一个面向 Windows 街机环境的全屏启动组件，用于模拟
SEGA ALLS 系统的 Logo Mode 与分阶段启动流程。

本次重构参考了 `sgxsegaboot` 可公开观察到的结构：WPF 界面、Logo Mode、
MVVM 状态驱动、多语言 STEP 资源、独立加载动画，以及由系统状态推进的启动序列。
本项目没有复制其受保护的程序代码、BAML 或专有运行时组件；Logo 与加载动画由
用户从合法持有的系统资源中提供。

## 主要变化

- 从单文件 Win32/GDI+ 迁移到 .NET 8 WPF，采用清晰的 Models、Services、
  ViewModels、Controls 与 Resources 分层。
- 界面基于固定 1280×720 设计画布等比缩放，在任意屏幕比例下自动留黑边，
  不再依赖特定图片尺寸。
- STEP 采用两位编号和 `01 → 04 → 21 → 30` 默认流程，内置参考系统中出现的
  STEP 01、04、10、11、12、13、21、30、31、32 文本。
- 中文、英文和日文资源完全分离，可通过配置或命令行切换。
- 启动时间线、目标程序、工作目录、退出行为、日志和显示模式均可配置。
- 启动批处理使用显式 `cmd.exe` 调用；普通可执行文件直接启动。缺少目标时显示
  明确错误而不是静默退出。
- 按参考程序的 `GifImage/GifImageView` 思路实现 GIF 逐帧播放，使用原始帧延迟，
  并在控件卸载时主动停止计时器。
- 原始 C++ 工程保存在 [`legacy/native-win32`](legacy/native-win32) 供追溯，
  不再参与解决方案构建。

## 构建

需要 Windows 10/11、Visual Studio 2022（含“.NET 桌面开发”工作负载）或
.NET 8 SDK。

```powershell
dotnet restore ALLS.sln
dotnet build ALLS.sln -c Release
```

发布为自包含的 Windows x64 目录：

```powershell
dotnet publish src/Alls.Bootstrapper/Alls.Bootstrapper.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true
```

## 使用

将发布产物与 `start.bat` 或 `启动.bat` 放在同一目录，然后运行 `ALLS.exe`。
程序默认全屏置顶、隐藏鼠标，完成 STEP 30 后启动找到的第一个批处理，并在
13 秒后关闭启动界面。按 `Esc` 可退出。

无需真正启动游戏即可预览界面：

```powershell
ALLS.exe --preview --windowed
```

其他参数：

- `--config <path>`：读取指定配置文件。
- `--language zh-CN|en-US|ja-JP`：覆盖界面语言。
- `--windowed`：覆盖为窗口模式。
- `--preview`：不运行外部程序且不自动关闭。

## 配置

默认配置位于
[`src/Alls.Bootstrapper/alls-launcher.json`](src/Alls.Bootstrapper/alls-launcher.json)，
构建时会复制到输出目录。除通过 `--config` 指定的配置文件路径外，配置中的相对路径
均以 `ALLS.exe` 所在目录为基准。

### 基础设置

```json
{
  "platformName": "ALLS HX2.1",
  "language": "zh-CN",
  "logoPath": "Assets/logo.png",
  "loadingPath": "Assets/logomode_load.gif",
  "autoCloseAfterSequence": true
}
```

| 字段 | 类型 | 作用 |
| --- | --- | --- |
| `platformName` | 字符串 | Logo 下方显示的平台名称 |
| `language` | 字符串 | `zh-CN`、`en-US` 或 `ja-JP`；其他值回退到中文 |
| `logoPath` | 字符串 | Logo 的相对路径或绝对路径 |
| `loadingPath` | 字符串 | Logo Mode 加载 GIF 的相对路径或绝对路径 |
| `autoCloseAfterSequence` | 布尔值 | 时间线正常结束或成功启动游戏后，是否自动关闭启动器 |

### display：显示设置

```json
"display": {
  "mode": "Fullscreen",
  "width": 1280,
  "height": 720,
  "topmost": true,
  "hideCursor": true,
  "allowEscapeToExit": true
}
```

| 字段 | 作用 |
| --- | --- |
| `mode` | `Fullscreen` 为全屏，`Windowed` 为窗口模式 |
| `width` / `height` | 窗口模式的宽高，全屏模式下由屏幕尺寸决定 |
| `topmost` | 是否始终置顶 |
| `hideCursor` | 是否隐藏鼠标指针 |
| `allowEscapeToExit` | 是否允许按 `Esc` 退出 |

界面使用 1280×720 的设计画布并保持比例缩放；屏幕比例不一致时，多余区域会显示
黑边，不会拉伸 Logo 和文字。

### launch：游戏启动设置

```json
"launch": {
  "enabled": true,
  "file": null,
  "candidates": ["start.bat", "启动.bat"],
  "arguments": "",
  "workingDirectory": ".",
  "waitForExit": false,
  "postLaunchDelayMs": 13000,
  "closeWhenTargetMissing": false
}
```

| 字段 | 作用 |
| --- | --- |
| `enabled` | 是否真正启动外部程序；设为 `false` 时只播放启动界面 |
| `file` | 指定唯一启动目标；为 `null` 时才会依次查找 `candidates` |
| `candidates` | 候选启动文件，按数组顺序使用第一个存在的文件 |
| `arguments` | 传递给启动目标的命令行参数 |
| `workingDirectory` | 启动目标的工作目录，同时也是候选文件的查找目录 |
| `waitForExit` | 是否等待游戏进程结束后再继续 |
| `postLaunchDelayMs` | 成功启动目标后，启动画面继续保留的毫秒数 |
| `closeWhenTargetMissing` | 找不到目标时是否关闭；为 `false` 时停留在错误画面 |

如果设置了 `file`，但该文件不存在，程序不会回退查找 `candidates`。例如直接启动
子目录里的游戏程序：

```json
"launch": {
  "enabled": true,
  "file": "Game.exe",
  "candidates": [],
  "arguments": "-fullscreen",
  "workingDirectory": "Game",
  "waitForExit": false,
  "postLaunchDelayMs": 5000,
  "closeWhenTargetMissing": false
}
```

`waitForExit` 为 `false` 时，`postLaunchDelayMs` 从游戏启动成功后开始计算；为
`true` 时，会先等待游戏进程退出，再计算这段延迟。街机启动场景通常应保持为
`false`。

### logging：日志设置

```json
"logging": {
  "enabled": true,
  "file": "Logs/alls-launcher.log"
}
```

日志记录程序启停、STEP 切换、实际启动目标和异常信息。日志写入失败不会阻止游戏
启动。

### timeline：启动时间线

```json
"timeline": [
  {
    "step": 1,
    "messageKey": "STEP_01_MESSAGE",
    "durationMs": 2200,
    "action": "Continue"
  }
]
```

| 字段 | 作用 |
| --- | --- |
| `step` | STEP 编号；界面按两位格式显示，例如 `1` 显示为 `STEP 01` |
| `messageKey` | 从当前语言资源读取的提示文本键 |
| `durationMs` | 当前阶段显示多久，单位为毫秒 |
| `action` | 阶段显示完成后执行的动作 |

`action` 支持以下值：

- `Continue`：进入下一个阶段。
- `Launch`：启动外部程序并结束时间线，后续阶段不会继续执行。
- `Exit`：不启动外部程序，直接结束时间线。

内置的多语言消息键：

| 消息键 | 中文文本 |
| --- | --- |
| `STEP_01_MESSAGE` | 启动中 |
| `STEP_04_MESSAGE` | 网络设置中 |
| `STEP_10_MESSAGE` | 请连接安装工具 |
| `STEP_11_MESSAGE` | 检索安装服务器 |
| `STEP_12_MESSAGE` | 游戏程序安装中 |
| `STEP_13_MESSAGE` | 请替换光盘 |
| `STEP_21_MESSAGE` | 游戏程序准备中 |
| `STEP_30_MESSAGE` | 稍后启动游戏程序 |
| `STEP_31_MESSAGE` | 稍后重启 |
| `STEP_32_MESSAGE` | 稍后重启 |

默认时间线为：

```text
STEP 01  启动中                 2.2 秒
STEP 04  网络设置中             2.2 秒
STEP 21  游戏程序准备中         2.4 秒
STEP 30  稍后启动游戏程序       1.2 秒
         ↓
启动 start.bat 或 启动.bat
         ↓
继续显示 13 秒后关闭
```

因此默认情况下，程序启动约 8 秒后运行游戏。如果需要模拟安装流程，可以在时间线
中加入 STEP 10、11、12、13；如果需要快速启动，则缩短各阶段的 `durationMs`。

例如约 2.5 秒后启动游戏：

```json
"timeline": [
  {
    "step": 1,
    "messageKey": "STEP_01_MESSAGE",
    "durationMs": 1000,
    "action": "Continue"
  },
  {
    "step": 21,
    "messageKey": "STEP_21_MESSAGE",
    "durationMs": 1000,
    "action": "Continue"
  },
  {
    "step": 30,
    "messageKey": "STEP_30_MESSAGE",
    "durationMs": 500,
    "action": "Launch"
  }
]
```

目前 STEP 04、10、12 等阶段属于视觉与时间模拟，并不会检测真实网络状态或安装
进度；真正启动外部程序的动作只有 `Launch`。

## 目录结构

```text
src/Alls.Bootstrapper/
├── Assets/          Logo、加载 GIF 与应用图标
├── Controls/        GIF 逐帧播放控件
├── Infrastructure/ 命令行解析
├── Models/          配置与启动阶段模型
├── Resources/       主题和三语言文本
├── Services/        配置、日志、进程与序列服务
├── ViewModels/      Logo Mode 状态
├── App.xaml         应用初始化
└── MainWindow.xaml  启动画面
```

## 声明

SEGA 与 ALLS 名称及相关商标归其权利人所有。本项目仅用于兼容性研究、界面模拟和
合法持有环境的启动整合，不包含原始 `sgxsegaboot.exe` 或 SEGA 系统组件。仓库中的
Logo 与加载动画由用户确认其已合法取得；使用者仍应自行确保其使用和分发方式符合
适用的授权条件。
