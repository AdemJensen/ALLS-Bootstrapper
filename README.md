# ALLS Bootstrapper

ALLS Bootstrapper 是面向 Windows 街机环境的全屏启动与守护组件，用于模拟 SEGA
ALLS 系统的 Logo Mode 和分阶段启动流程。程序使用 .NET 8 WPF，支持多游戏菜单、
SEGA IO4/ADX HID/NPro DX/Maimoller IO 机台输入、独立的启动脚本与游戏目标监控，以及游戏退出后
自动恢复启动器界面。

界面结构与启动表现参考了 `sgxsegaboot`；项目没有复制其程序代码、BAML 或专有运行时
组件。仓库中的 Logo 与加载动画由用户从合法持有的系统资源中提供。

## 功能

- 可选择启动时直接运行默认游戏，或进入“操作界面”。
- 启动中按 1P/2P `SELECT` 进入操作界面。
- 启动中按 4 号或 5 号键跳过前置阶段，显示最后一个 STEP 并立即启动游戏。
- 操作界面使用 1 号键上移、4 号键下移、5 号键确认。
- 每个游戏可单独配置启动脚本、参数、工作目录、STEP 时间线、等待进程和等待窗口。
- 启动脚本完成后继续等待真正的游戏进程/窗口；游戏就绪后隐藏界面并留在后台守护。
- 指定进程结束或窗口消失后，按配置显示操作界面或错误页。
- 自动识别横屏和竖屏；竖屏使用 maimai 机台式下部显示区，不再出现横向白色条带。
- 支持中文、英文和日文资源，支持窗口预览、日志与键盘调试。

## 构建

需要 Windows 10/11、Visual Studio 2022（含“.NET 桌面开发”工作负载）或 .NET 8 SDK。

```powershell
dotnet restore ALLS.sln
dotnet build ALLS.sln -c Release
```

发布为自包含 Windows x64 程序：

```powershell
dotnet publish src/Alls.Bootstrapper/Alls.Bootstrapper.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true
```

程序通过 HidSharp 读取 Raw HID；首次还原项目时需要从 NuGet 获取该包。

## 使用与按键

默认配置会查找 `ALLS.exe` 同目录的 `start.bat` 或 `启动.bat`，然后等待名为
`Sinmai.exe` 的进程及其可见窗口。不同版本的游戏可执行文件名可能不同，实际部署前
请修改 `games[].monitor`。

| 场景 | 机台按键 | 键盘调试键 |
| --- | --- | --- |
| 启动中进入操作界面 | 1P/2P `SELECT` | `S` 或 `Tab` |
| 启动中跳至最后阶段并启动 | 4 号或 5 号 | `4`/↓ 或 `5`/Enter/Space |
| 操作界面上移 | 1 号 | `1`/↑/小键盘 8 |
| 操作界面下移 | 4 号 | `4`/↓/小键盘 2 |
| 操作界面确认 | 5 号 | `5`/Enter/Space |
| 退出启动器 | — | `Esc`（需允许） |

命令行参数：

- `--config <path>`：使用指定 JSON 文件。
- `--language zh-CN|en-US|ja-JP`：覆盖语言。
- `--windowed`：覆盖为窗口模式。
- `--preview`：不执行游戏或操作命令，适合界面调试。

例如：

```powershell
ALLS.exe --preview --windowed
```

## JSON 配置说明

默认文件是
[`src/Alls.Bootstrapper/alls-launcher.json`](src/Alls.Bootstrapper/alls-launcher.json)，
构建时会复制到输出目录。除 `--config` 指定的配置文件本身外，配置中的相对路径都以
`ALLS.exe` 所在目录为基准。枚举值不区分大小写，示例中的拼写最直观。

### 顶层字段

| 字段 | 含义 |
| --- | --- |
| `platformName` | Logo 下方的平台名 |
| `language` | `zh-CN`、`en-US` 或 `ja-JP` |
| `logoPath` | Logo 图片路径 |
| `loadingPath` | 加载 GIF 路径 |
| `display` | 窗口和显示行为 |
| `startup` | 启动入口与默认游戏 |
| `input` | IO4/HID 和键盘输入 |
| `logging` | 日志设置 |
| `timeline` | 所有游戏共用的默认 STEP 时间线 |
| `games` | 可从操作界面选择的游戏数组 |
| `operations` | 可从操作界面执行的其他操作数组 |

### display：显示设置

```json
"display": {
  "mode": "Fullscreen",
  "layoutMode": "Auto",
  "width": 1280,
  "height": 720,
  "topmost": true,
  "hideCursor": true,
  "allowEscapeToExit": true
}
```

`mode` 可为 `Fullscreen` 或 `Windowed`。`width`、`height` 只控制窗口模式；全屏模式
使用当前主屏幕尺寸。`layoutMode` 支持：

- `Auto`：宽度大于等于高度时使用普通横屏；高度大于宽度时自动使用机台布局。
- `Landscape`：始终将 1280×720 设计画布等比放入整个窗口。
- `Cabinet`：使用 maimai 机台式显示区。竖屏时在底部建立最大正方形区域，并把
  Logo Mode、操作界面和错误页放在该区域中央；横屏时使用偏右的机台显示区。

在 1080×1920 屏幕上，`Auto`/`Cabinet` 会得到底部约 1080×1080 的机台区域，内容
按照 1280×720 比例放在其中，上方和周围由窗口本身的白色背景填满，因此不会再出现
黑色背景中的横向白条。`topmost`、`hideCursor`、`allowEscapeToExit` 分别控制置顶、
隐藏鼠标和允许 `Esc` 退出。

### startup：启动入口

```json
"startup": {
  "mode": "DefaultGame",
  "defaultGameId": "maimai-dx",
  "allowSelectToOpenMenu": true
}
```

- `mode`：`DefaultGame` 直接启动默认游戏；`Menu` 一启动就显示操作界面。
- `defaultGameId`：`DefaultGame` 模式使用的 `games[].id`。
- `allowSelectToOpenMenu`：启动阶段是否允许 1P/2P `SELECT` 中断并进入操作界面。

### input：maimai DX IO4/HID/Maimoller

```json
"input": {
  "enabled": true,
  "keyboardFallback": true,
  "hotPlugIntervalMs": 1000,
  "devices": [
    {
      "enabled": true,
      "name": "SEGA IO4",
      "profile": "SegaIo4",
      "player": 0,
      "deviceIndex": 0,
      "vendorId": "0x0CA3",
      "productIds": ["0x0021"]
    }
  ]
}
```

- `enabled`：总开关。
- `keyboardFallback`：是否启用上表中的键盘调试键。
- `hotPlugIntervalMs`：未找到设备或设备断开后的重新扫描间隔。
- `devices`：要监听的 HID 设备。`vendorId`、`productIds` 可使用 `0x` 十六进制；
  `player` 为 `1` 或 `2`，`0` 表示由报告/产品 ID 判断或同时读取两侧。
- `deviceIndex`：连接了多个相同 VID/PID 的设备时，从 `0` 开始选择第几个；设备按
  Windows HID 路径稳定排序。默认配置用它读取第二块 IO4。
- `profile`：`SegaIo4`、`AdxHid`、`NProDx` 或 `Maimoller`，决定 HID 报告的解析方式。

默认同时兼容以下常见标识：

| 设备 | VID:PID | 玩家 |
| --- | --- | --- |
| SEGA IO4 | `0CA3:0021` | 首块报告内读取 1P/2P，并兼容第二块设备的 2P 输入 |
| ADX HID | `2E3C:5750` / `2E4C:5750` | 1P / 2P |
| NPro DX | `2E3C:5751` / `2E3C:5752` | 1P / 2P |
| Maimoller IO V2 | `0E8F:1224` | 从 feature report 自动识别 1P / 2P |

Maimoller 的 1P 和 2P 使用相同 VID/PID。请分别保留两项配置并将 `player` 设为 `1`
和 `2`；启动器会限定到复合设备的 `MI_00` 接口，并读取 report ID 1 的 feature report
判断控制器属于哪一侧，因此不依赖 Windows 的枚举顺序。默认配置已经包含：

```json
{
  "enabled": true,
  "name": "Maimoller IO V2 1P",
  "profile": "Maimoller",
  "player": 1,
  "deviceIndex": 0,
  "vendorId": "0x0E8F",
  "productIds": ["0x1224"]
}
```

输入按“未按 → 按下”的边沿触发；一直按住按钮只执行一次，松开后再次按下才会再次
触发。若设备固件使用不同 VID/PID，可在 `devices` 中追加条目；若报告布局不同，则还
需要新增相应解析 profile。执行游戏程序前，启动器会关闭并释放所有 HID 连接，避免
与游戏侧的 AquaMai/IO 模块争用设备；游戏目标消失、启动失败或返回菜单时会自动重连。

### games：游戏、启动脚本与目标监控

```json
"games": [
  {
    "id": "maimai-dx",
    "title": "maimai DX",
    "description": "启动 maimai DX 游戏程序",
    "launch": {
      "enabled": true,
      "file": null,
      "candidates": ["start.bat", "启动.bat"],
      "arguments": "",
      "workingDirectory": ".",
      "waitForExit": false
    },
    "monitor": {
      "processNames": ["Sinmai"],
      "window": {
        "enabled": true,
        "processName": "Sinmai",
        "titleContains": "",
        "className": ""
      },
      "readyMode": "All",
      "exitMode": "AnyMissing",
      "startupTimeoutMs": 120000,
      "pollIntervalMs": 500
    },
    "timeline": [],
    "onExit": "Menu",
    "exitErrorTitle": "GAME PROGRAM ENDED",
    "exitErrorMessage": "游戏程序已经停止运行"
  }
]
```

每个游戏的字段：

| 字段 | 含义 |
| --- | --- |
| `id` | 唯一标识，同时供 `startup.defaultGameId` 引用 |
| `title` / `description` | 操作界面显示的标题与说明 |
| `launch` | 实际执行的启动器、批处理或脚本；它可以与被监控的游戏完全不同 |
| `monitor` | 真正游戏进程/窗口的就绪与退出判断 |
| `timeline` | 此游戏独有的时间线；空数组表示使用顶层 `timeline` |
| `onExit` | 游戏目标消失后显示 `Menu` 或 `Error` |
| `exitErrorTitle` / `exitErrorMessage` | `onExit` 为 `Error` 时显示的内容 |

`launch` 字段：

| 字段 | 含义 |
| --- | --- |
| `enabled` | `false` 时不真正执行，用于预览 |
| `file` | 唯一目标文件；非空时不会回退到候选列表 |
| `candidates` | `file` 为空时按顺序查找，使用第一个存在的文件 |
| `arguments` | 传给目标的命令行参数 |
| `workingDirectory` | 工作目录，也是相对候选文件的查找目录 |
| `waitForExit` | 是否等待这个“启动脚本”退出；通常应为 `false` |

`monitor.processNames` 是同一游戏可能使用的进程名候选，任意一个存在即表示进程条件
成立，可写 `Sinmai` 或 `Sinmai.exe`。窗口条件可同时按所属 `processName`、标题包含
`titleContains`、精确窗口类名 `className` 过滤；空字符串表示不限制该项。

`readyMode` 决定启动成功：

- `All`：配置了进程和窗口时，两者都出现才隐藏启动器。
- `Any`：其中任意一个出现即可。

`exitMode` 决定何时唤回启动器：

- `AnyMissing`：任一已配置条件消失就返回，适合同时严格监视进程和窗口。
- `AllMissing`：所有已配置条件都消失才返回，适合窗口会重建的游戏。

`startupTimeoutMs` 是等待游戏目标出现的最长时间，超时进入错误页；
`pollIntervalMs` 是检测间隔。至少配置一个进程或窗口条件，否则游戏会被立即视为就绪，
而守护程序会一直留在后台直到程序被手动退出。

添加第二个游戏只需在 `games` 数组追加对象，并给它不同的 `id`、`launch` 和
`monitor`。操作界面会自动按数组顺序列出。

### timeline：STEP 时间线

```json
"timeline": [
  {
    "step": 1,
    "messageKey": "STEP_01_MESSAGE",
    "durationMs": 2200,
    "action": "Continue"
  },
  {
    "step": 30,
    "messageKey": "STEP_30_MESSAGE",
    "durationMs": 1200,
    "action": "Launch"
  }
]
```

- `step`：两位显示的 STEP 编号。
- `messageKey`：从当前语言资源读取的文字键。
- `durationMs`：显示时长（毫秒）。
- `action`：`Continue` 进入下一阶段，`Launch` 执行当前游戏的 `launch`，`Exit` 退出。

内置消息键为 `STEP_01_MESSAGE`、`STEP_04_MESSAGE`、`STEP_10_MESSAGE`、
`STEP_11_MESSAGE`、`STEP_12_MESSAGE`、`STEP_13_MESSAGE`、`STEP_21_MESSAGE`、
`STEP_30_MESSAGE`、`STEP_31_MESSAGE` 和 `STEP_32_MESSAGE`。4 号/5 号跳过功能会定位
时间线中最后一个 `Launch` 阶段。

### operations：其他操作

```json
"operations": [
  {
    "id": "exit",
    "title": "退出启动器",
    "description": "关闭 ALLS Bootstrapper",
    "kind": "Exit",
    "command": {
      "enabled": false,
      "file": null,
      "candidates": [],
      "arguments": "",
      "workingDirectory": ".",
      "waitForExit": false
    },
    "closeAfterRun": false
  }
]
```

`kind` 为 `Exit` 时直接退出，忽略 `command`；为 `Command` 时按与游戏 `launch` 相同
的规则执行命令。`closeAfterRun` 决定命令成功启动后是否关闭 ALLS，否则返回操作界面。

### logging：日志

```json
"logging": {
  "enabled": true,
  "file": "Logs/alls-launcher.log"
}
```

日志记录程序启停、STEP、HID 连接、按钮事件、实际启动目标，以及进程/窗口目标的就绪
和消失。日志写入失败不会阻止游戏启动。

## 运行流程

```text
默认游戏或操作界面
        ↓ 选择游戏
播放该游戏的 STEP 时间线（4/5 可跳至 Launch）
        ↓
运行 launch 中的批处理/程序
        ↓
等待 monitor 中真正的游戏进程/窗口
        ↓ 就绪
隐藏界面，在后台守护
        ↓ 进程结束或窗口消失
按 onExit 显示操作界面或错误页
```

## 目录结构

```text
src/Alls.Bootstrapper/
├── Assets/          Logo、加载 GIF 与应用图标
├── Controls/        GIF 逐帧播放控件
├── Infrastructure/ 命令行解析
├── Models/          JSON 配置与启动阶段模型
├── Resources/       主题和三语言文本
├── Services/        HID、监控、启动、日志与配置服务
├── ViewModels/      启动/菜单/错误状态机
├── App.xaml         应用初始化
└── MainWindow.xaml  启动与操作界面
```

原始 C++ 工程保存在 `legacy/native-win32` 供追溯，不参与当前解决方案构建。

## 实现参考

- [ALLS-Boot-Simulator 的横屏/机台布局计算](https://github.com/Songyuhao114514/ALLS-Boot-Simulator/blob/main/src/SgxSegaBoot/AllsBootWindow.cs)
- [segatools 的 SEGA IO4 报告结构](https://github.com/djhackersdev/segatools/blob/master/board/io4.c)
- [ADX IO4/HID 按键报告示例](https://github.com/alfredodeux/adx-mai2io-dll)
- [AquaMai 的 ADX/NPro 设备识别与报告映射](https://github.com/MuNET-OSS/AquaMai/blob/main/AquaMai.Mods/GameSystem/AdxHidInput.cs)
- [AquaMai 的 Maimoller IO V2 原生 HID 实现](https://github.com/MuNET-OSS/AquaMai/blob/main/AquaMai.Mods/GameSystem/MaimollerIO/Libs/MaimollerDeviceNative.cs)
- [HidSharp 2.6.4](https://www.nuget.org/packages/HidSharp/2.6.4)

## 声明

SEGA、ALLS 与 maimai 名称及相关商标归其权利人所有。本项目仅用于兼容性研究、界面
模拟和合法持有环境的启动整合，不包含原始 `sgxsegaboot.exe` 或 SEGA 系统组件。
仓库中的 Logo 与加载动画由用户确认其已合法取得；使用者仍应自行确保使用和分发方式
符合适用授权条件。
