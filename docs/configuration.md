# JSON 配置参考

本文档说明 ALLS Bootstrapper 的 `alls-launcher.json` 字段、枚举值、引用关系和行为。
日常编辑建议使用图形配置器；需要批量生成、版本管理或排查底层行为时可直接编辑 JSON。

默认文件是
[`src/Alls.Bootstrapper/alls-launcher.json`](../src/Alls.Bootstrapper/alls-launcher.json)，
构建时会复制到输出目录。除 `--config` 指定的配置文件本身外，配置中的相对路径都以
`ALLS.exe` 所在目录为基准。枚举值不区分大小写，示例中的拼写最直观。

返回 [项目 README](../README.md)，或阅读 [Configurator 设计说明](configurator.md)。

## 顶层字段

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
| `updateSources` | HTTP/U 盘更新来源列表 |
| `games` | 可从操作界面选择的游戏数组 |
| `operations` | 可从操作界面执行的其他操作数组 |

## display：显示设置

```json
"display": {
  "mode": "Fullscreen",
  "layoutMode": "MaimaiDx",
  "width": 1280,
  "height": 720,
  "stepTransitionMs": 0,
  "topmost": true,
  "hideCursor": true,
  "allowEscapeToExit": true
}
```

`mode` 可为 `Fullscreen` 或 `Windowed`。`width`、`height` 只控制窗口模式；全屏模式
使用当前主屏幕尺寸。`layoutMode` 是操作界面和未指定游戏布局时使用的默认预设：

| `layoutMode` | 适用机台 | 布局行为 |
| --- | --- | --- |
| `Chunithm` | Chunithm 等横屏机台 | 1280×720 设计画布等比使用整个屏幕 |
| `MaimaiDx` | maimai DX（SDEZ / SDGB / SDGA） | 在 1080×1920 竖屏最下方建立紧贴左、右、下边缘的 1080×1080 主显示圆区域，空出顶部副屏位置 |
| `Ongeki` | Ongeki | 不预留顶部副屏，启动内容在整块竖屏中央显示 |
| `CardMaker` | Card Maker | 不预留顶部副屏，独立保留为后续微调的预设 |
| `Auto` | 自动判断 | 横屏使用 `Chunithm`，竖屏使用 `MaimaiDx` |

旧配置中的 `Landscape` 与 `Cabinet` 仍然兼容，分别等同于 `Chunithm` 和
`MaimaiDx`。竖屏预设会使用放大的 Logo 和状态文字，以补偿 1280×720 设计画布缩放到
1080 像素宽度时产生的视觉缩小。窗口其余区域仍由白色背景覆盖，因此不会出现黑底中的
横向白条。`topmost`、`hideCursor`、`allowEscapeToExit` 分别控制置顶、隐藏鼠标和允许
`Esc` 退出。`stepTransitionMs` 控制 STEP/消息切换时的淡入毫秒数；`0` 表示完全关闭
渐变，也是默认值。窗口模式保持配置的固定尺寸，同时显示系统标题栏和任务栏按钮，可拖动和
最小化。全屏启动器不创建任务栏按钮；检测到游戏窗口后会先将前台焦点交给
游戏再隐藏自身。游戏进程或窗口消失后，启动器会重新覆盖任务栏并恢复键盘焦点，无需用
鼠标点击窗口。

启动阶段的消息（包括 STEP 12 更新进度）会按提示区宽度自动换行，多行文字居中显示。
加载动画位于文字左侧，始终对齐第一行，不会随文字行数增加而移到中间。
提示区保持当前布局的字号和位置，
换行后向下延伸到屏幕（窗口模式下为客户区）底部，不会因消息行数增加而自动缩小，
也不受 1280×720 设计画布的底部限制。报错页面的标题和错误详情使用红色，底部不显示操作提示。

## startup：启动入口

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

## input：maimai DX IO4/HID/Maimoller

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

## games：游戏、启动脚本与目标监控

```json
"games": [
  {
    "id": "maimai-dx",
    "title": "maimai DX",
    "description": "启动 maimai DX 游戏程序",
    "layoutMode": "MaimaiDx",
    "language": "ja-JP",
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
      "pollIntervalMs": 500,
      "readyDelayMs": 5000
    },
    "update": {
      "enabled": false,
      "sourceIds": ["usb-main", "remote-main"],
      "applyMode": "ReplaceExisting",
      "targetDirectory": ".",
      "versionFile": "ABU_VERSION"
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
| `layoutMode` | 可选的机型布局预设；省略时继承 `display.layoutMode` |
| `language` | 可选的 Timeline 语言：`zh-CN`、`en-US`、`ja-JP`；空字符串或省略时继承顶层 `language` |
| `launch` | 实际执行的启动器、批处理或脚本；它可以与被监控的游戏完全不同 |
| `monitor` | 真正游戏进程/窗口的就绪与退出判断 |
| `update` | 此游戏的更新来源顺序、复制策略与目标目录 |
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

`startupTimeoutMs` 是等待游戏目标出现的最长时间，超时进入错误页；`pollIntervalMs` 是
检测间隔；`readyDelayMs` 是目标首次满足条件后继续显示 STEP 30 的时间。延迟结束后启动器
才隐藏。至少配置一个进程或窗口条件，否则游戏会被立即视为就绪，而守护程序会一直留在
后台直到程序被手动退出。

`games[].language` 只影响该游戏的 STEP 消息，包括更新阶段与等待游戏出现期间保持显示的
STEP 30。操作菜单、确认页和错误页始终使用顶层 `language`，因此从日文游戏返回中文菜单时
不需要重新加载界面资源。

添加第二个游戏只需在 `games` 数组追加对象，并给它不同的 `id`、`layoutMode`、
`launch` 和 `monitor`。操作界面会自动按数组顺序列出。例如同一台电脑可以混合配置：

```json
{
  "id": "chunithm",
  "title": "Chunithm",
  "layoutMode": "Chunithm"
},
{
  "id": "ongeki",
  "title": "Ongeki",
  "layoutMode": "Ongeki"
},
{
  "id": "card-maker",
  "title": "Card Maker",
  "layoutMode": "CardMaker"
}
```

以上是字段重点示例；实际对象仍需包含 `launch`、`monitor` 等配置。

## updateSources 与游戏自动更新

更新来源是顶层列表，每个来源由唯一 `id` 标识。HTTP 与 U 盘来源示例：

```json
"updateSources": [
  {
    "id": "remote-main",
    "enabled": true,
    "kind": "Http",
    "baseUrl": "http://192.168.1.10:8000/",
    "username": "sega",
    "password": "password",
    "path": "sega_game_updates",
    "containsMultipleGames": true,
    "requestTimeoutMs": 300000
  },
  {
    "id": "usb-main",
    "enabled": true,
    "kind": "Usb",
    "driveLetter": "F:",
    "path": "sega_game_updates",
    "containsMultipleGames": true
  }
]
```

| 字段 | 含义 |
| --- | --- |
| `kind` | `Http` 或 `Usb` |
| `baseUrl` | HTTP 文件服务器根地址 |
| `username` / `password` | 可选的 HTTP Basic Auth；留空表示不认证 |
| `driveLetter` | U 盘盘符，可写 `F` 或 `F:` |
| `path` | 服务器或 U 盘中的更新根目录 |
| `containsMultipleGames` | `true` 时在根目录下继续寻找以 `games[].id` 命名的目录；`false` 时根目录内容直接属于该游戏 |
| `requestTimeoutMs` | HTTP 请求等待响应的超时时间；目录内容读取也使用此超时值 |
| `unavailableIsFailure` | 仅 HTTP 来源使用，默认 `true`；首次请求连接不可用时是否将该来源记为失败 |

HTTP 服务器需要开启目录索引；启动器会解析常见的 Python `http.server`、Apache 和
Nginx 风格链接并递归下载。Basic Auth 密码以明文保存在 JSON 中，应限制配置文件权限，
并优先在非可信网络上使用 HTTPS。

个人电脑上的 HTTP 服务器只在需要更新时启动，可为该来源设置
`"unavailableIsFailure": false`。此时首次请求在收到 HTTP 响应前发生连接错误、DNS
解析失败或等待响应超时，会显示“服务器连接不可用，按配置跳过”，记入普通日志并继续
尝试下一个来源；全部来源都这样跳过时，游戏显示黄色的“服务器连接不可用，已跳过”，
批量更新不将其计为失败。设为 `true` 时，同类情况显示“服务器连接失败”并计入失败。
旧 JSON 未填写此字段时保持原有的失败行为；USB 来源忽略此字段。

无法仅凭连接错误判断服务器进程是否未启动，因此界面使用“连接不可用”。该设置只作用于
首次请求在收到响应前的网络错误；TLS、证书、HTTP 权限、服务端错误及协议错误不会被忽略。
收到响应后，读取目录内容、后续子目录请求、文件下载或复制出错，始终计为更新失败。
首个游戏根目录请求返回 404 仍归类为“找不到对应的更新目录”；子目录或文件下载时返回
404 则算更新失败，并在详情中注明出错的目录或文件 URL。用户主动中断仍按取消处理。

例如，临时更新服务器可以配置为：

```json
{
  "id": "temporary-http",
  "enabled": true,
  "kind": "Http",
  "baseUrl": "http://192.168.1.10:8000/",
  "path": "sega_game_updates",
  "containsMultipleGames": true,
  "requestTimeoutMs": 5000,
  "unavailableIsFailure": false
}
```

每个游戏通过 `update` 选择来源和行为：

```json
"update": {
  "enabled": true,
  "sourceIds": ["usb-main", "remote-main"],
  "applyMode": "ReplaceExisting",
  "targetDirectory": "Package/Sinmai_Data/StreamingAssets",
  "versionFile": "ABU_VERSION"
}
```

来源会按 `sourceIds` 顺序尝试，首个成功或版本一致的来源结束本次更新。所有来源都失败时
只写入日志，游戏仍继续启动。`targetDirectory` 的相对路径以该游戏的
`launch.workingDirectory` 为基准，也可以填写绝对路径。

自动更新在时间线中的安装阶段（STEP 12）执行。默认顺序为 STEP 01 → STEP 04 →
STEP 12 更新 → STEP 21 → STEP 30 启动，更新结束后接着播放后续阶段。
启用更新时，运行时会在首个编号不小于 12 的阶段前插入 STEP 12；若该阶段本身就是
STEP 12，则直接在其中更新。时间线提前遇到 `Launch` 或 `Exit` 时，更新会先于该动作
执行；所有阶段均小于 12 且无结束动作时，更新放在时间线末尾。此过程不改写 JSON，
关闭更新则按原时间线播放。`--preview` 不执行更新。

HTTP 来源会先递归读取所有目录列表并统计文件总数，再开始下载；子目录文件计入同一总数，
重复链接和 `.DS_Store` 不重复计数或下载。STEP 12 会在原提示文字后显示当前阶段和
百分比，例如“游戏程序安装中 · 正在下载 50%”。检查更新源、尝试连接和统计文件列表时
不显示进度后缀；开始下载或复制后分别按
已完成文件数 / 该阶段文件总数计算，切换阶段时进度重新从 0% 开始，成功完成或版本已最新
时显示 100%。这是文件数量进度，单个大文件传输期间百分比会保持不变。

`applyMode` 支持：

- `FullReplace`：删除目标目录中的原内容，再完整复制来源。为了安全，不允许目标为磁盘根目录。
- `ReplaceExisting`：复制来源中的全部文件和目录；覆盖同名文件，但保留来源中不存在的本地项。
- `AddNewOnly`：只复制目标中尚不存在的文件，不覆盖任何现有文件。

`FullReplace` 和 `ReplaceExisting` 会检查来源根目录中的 `versionFile`。来源和目标都存在
该文件且去除首尾空白后的内容一致时，本次更新直接视为成功并跳过复制；来源没有版本文件
或版本不一致时正常更新。`AddNewOnly` 不进行版本短路。更新时会忽略来源任意目录中的
`.DS_Store`，该文件不会被下载、计数或复制。`versionFile` 会在其他文件全部复制成功后最后
写入，并先写到同目录临时文件再替换正式版本文件；更新失败或被中断时不会提前提交新版本号，
所以下次仍会重新尝试更新。

例如 `maimai-dx-magical` 的目标为
`D:\maimai\sdez170\Package\Sinmai_Data\Streaming_Assets`，U 盘来源为
`F:\sega_game_updates\maimai-dx-magical`，可配置为：

```json
{
  "id": "maimai-dx-magical",
  "launch": {
    "workingDirectory": "D:/maimai/sdez170"
  },
  "update": {
    "enabled": true,
    "sourceIds": ["usb-main"],
    "applyMode": "ReplaceExisting",
    "targetDirectory": "Package/Sinmai_Data/Streaming_Assets",
    "versionFile": "ABU_VERSION"
  }
}
```

若来源含 `A001`、`A002`、`A003` 和 `ABU_VERSION`，本地含 `A000`、`A001`、`A002`，
版本不一致时会覆盖 `A001`、`A002` 和版本文件，新增 `A003`，并保留本地 `A000`。

## timeline：STEP 时间线

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
时间线中最后一个 `Launch` 阶段；若自动更新尚未执行，会先完成 STEP 12 更新，再跳到
`Launch`。更新期间的跳过或 SELECT 请求会在更新结束后处理，避免中途截断文件复制。

## operations：机台操作

```json
"operations": [
  {
    "id": "shutdown",
    "title": "关闭机台电源",
    "description": "关闭 Windows 与机台电源",
    "kind": "Shutdown",
    "command": {
      "enabled": true,
      "file": null,
      "candidates": [],
      "arguments": "",
      "workingDirectory": ".",
      "waitForExit": false
    },
    "confirmation": {
      "enabled": true,
      "title": "关闭机台电源？",
      "message": "该操作无法撤销，请确认。"
    },
    "closeAfterRun": false
  }
]
```

`kind` 支持 `UpdateAllGames`、`Shutdown`、`Restart`、`Exit` 和 `Command`。
`UpdateAllGames` 按游戏配置顺序依次检查所有游戏；未开启更新的游戏也会显示为
“未开启更新”，单个游戏更新失败不会阻止后续游戏。确认执行后会进入“正在升级中”页面，
页面会立即显示所有游戏；当前游戏会依次显示检查、统计文件、下载（HTTP 来源）、统计复制
文件和复制状态。获取完整文件列表前显示不定进度；下载和复制时分别实时显示文件总数、
已完成文件数和百分比，进度条按文件数量推进，进行中状态使用蓝色。更新完成或失败后会隐藏
该游戏的进度条，但详细信息中仍会保留本次更新的文件总数和成功复制数。每个游戏最终会显示“等待中 / 未开启更新 /
没有可用更新源 / 找不到对应的更新目录 / 服务器连接不可用，已跳过 / 版本已最新，无需更新 / 已完成更新 / 更新失败”
之一。成功项显示绿色，无更新或无需更新
显示黄色，未开启显示灰色，失败显示红色。全部处理完毕后页面会保留，不会自动返回
操作界面。USB 目录不存在或首次游戏根目录 HTTP 请求返回 404 时会归类为“找不到对应的更新目录”，而不是
更新失败，并继续尝试列表中的下一个更新源。

更新期间底部显示“中断更新并返回”，也可以按 SELECT 发起中断。复制流程到达可暂停点后，
界面会先显示已暂停，再询问是否确认中断，并提示游戏文件可能损坏或不完整；取消中断会从
暂停点继续。更新完成后底部恢复为“返回操作界面”。同时按 2+7 可在游戏列表和返回按钮间切换；1/4
号键也会在到达列表首尾时进入返回按钮，并可从返回按钮重新进入列表。在游戏列表中按
5 号键显示或隐藏详细信息。详细信息会列出每个更新源是
成功、版本一致、未配置、未启用、未尝试，还是执行失败；失败项同时显示
具体错误内容。更新进行期间可以查看状态，也可以通过上述暂停确认流程中断并返回。

`Shutdown`、`Restart` 调用 Windows 系统电源操作；
`Exit` 只关闭启动器；`Command` 按与游戏 `launch` 相同的规则执行命令。
`confirmation.enabled` 决定是否显示二次确认，`title`、`message` 留空时使用当前语言的
默认文字，也可自行覆盖。确认页默认选中“取消”，使用 1/4 切换、5 确认、SELECT 取消。
`closeAfterRun` 决定普通命令成功启动后是否关闭 ALLS，否则返回操作界面。

## logging：日志

```json
"logging": {
  "enabled": true,
  "file": "Logs/alls-launcher.log"
}
```

日志记录程序启停、STEP、HID 连接、按钮事件、实际启动目标，以及进程/窗口目标的就绪
和消失。日志写入失败不会阻止游戏启动。
