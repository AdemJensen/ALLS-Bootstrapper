# ALLS Bootstrapper

ALLS Bootstrapper 是面向 Windows 街机环境的全屏启动与守护组件，用于模拟 SEGA
ALLS 系统的 Logo Mode 和分阶段启动流程。项目使用 .NET 8 WPF，包含两个独立程序：

- **ALLS Bootstrapper**：负责游戏更新、启动、目标监控、机台输入和游戏退出后的界面恢复。
- **ALLS Configurator**：用于图形化编辑、校验和保存 Bootstrapper 的 JSON 配置。

界面结构与启动表现参考了 `sgxsegaboot`；项目没有复制其程序代码、BAML 或专有运行时
组件。仓库中的 Logo 与加载动画由用户从合法持有的系统资源中提供。

## 主要功能

- 支持启动默认游戏或直接进入操作界面。
- 支持多游戏菜单以及关机、重启、退出、自定义命令和批量更新操作。
- 每个游戏可独立配置布局、语言、启动命令、STEP 时间线和目标监控规则。
- 支持 HTTP 文件服务器与 U 盘更新源，并提供三种文件应用策略。
- 支持 SEGA IO4、ADX HID、NPro DX 和 Maimoller IO V2。
- 支持 Chunithm、maimai DX、Ongeki、Card Maker 和自动布局预设。
- 支持中文、英文和日文资源、窗口预览、日志及键盘调试。
- 游戏目标就绪后自动隐藏，目标消失后按配置返回菜单或显示错误页。

完整 JSON 字段、示例和更新规则请参阅
[JSON 配置参考](docs/configuration.md)。

## 图形配置器

`ALLS.Configurator.exe` 覆盖当前 JSON 模型中的全部配置项，包括：

- 品牌资源、语言、窗口与启动入口；
- 游戏、启动命令、进程/窗口监控和专用时间线；
- HID 输入设备；
- HTTP/U 盘更新源；
- 关机、重启、退出、批量更新和自定义机台操作；
- 默认 STEP 时间线及日志。

游戏、设备、更新源、操作和时间线均支持添加、复制、删除与排序。保存前会检查 ID 唯一性、
跨对象引用、数值范围、URL、VID/PID 和命令完整性；错误会阻止保存，警告只作提示。覆盖
已有文件时，会先在同目录生成 `<配置文件>.bak`。

配置器接受 JSON 文件路径作为启动参数：

```powershell
ALLS.Configurator.exe .\alls-launcher.json
```

如果未传入路径，配置器会自动尝试打开自身目录中的 `alls-launcher.json`。常用快捷键：

| 操作 | 快捷键 |
| --- | --- |
| 新建配置 | `Ctrl+N` |
| 打开配置 | `Ctrl+O` |
| 保存 | `Ctrl+S` |
| 另存为 | `Ctrl+Shift+S` |

配置器的内部结构、保存策略和扩展步骤见
[Configurator 设计说明](docs/configurator.md)。

## 环境与构建

程序运行需要 Windows 10/11。项目可以在 Windows 或 macOS 上使用 .NET 8 SDK 构建；
Windows 用户也可以使用 Visual Studio 2022（含“.NET 桌面开发”工作负载）。

```powershell
dotnet restore ALLS.sln
dotnet build ALLS.sln -c Release
```

Bootstrapper 使用 HidSharp 读取 Raw HID，首次还原时需要从 NuGet 获取该包。

### 合并发布两个完全自包含单文件（推荐）

根目录的 `Makefile` 会将两个项目发布到 `artifacts/ALLS-win-x64`，然后生成
`artifacts/ALLS-win-x64.zip`。macOS 自带 `make`；Windows 需要 GNU Make，可通过
Chocolatey、Scoop、MSYS2 或 Git for Windows 环境安装。

自动选择当前平台：

```console
make publish
```

也可以显式选择平台：

```console
# Windows：调用 PowerShell 与 Compress-Archive
make publish-win

# macOS：调用 bash 与系统自带的 ditto
make publish-mac
```

如果 `dotnet` 没有加入 `PATH`，可以覆盖 `DOTNET`；也可以通过 `CONFIGURATION` 改变构建配置：

```console
# macOS 示例
make publish-mac DOTNET=/private/tmp/alls-dotnet-sdk/dotnet

# Windows PowerShell 示例
make publish-win DOTNET="C:\Program Files\dotnet\dotnet.exe" CONFIGURATION=Release
```

其他目标：

```console
make build
make help
```

`PublishSingleFile` 将托管运行库嵌入 EXE，`IncludeNativeLibrariesForSelfExtract` 继续把 WPF
原生运行库一并嵌入；启动时它们会自动解压到系统临时目录。最终发布目录只包含
`ALLS.exe`、`ALLS.Configurator.exe`、`alls-launcher.json` 和 `Assets` 中的两项资源，
目标机器无需安装 .NET，也无需保留散落的运行库 DLL。

`EnableCompressionInSingleFile` 会压缩 EXE 内嵌的托管程序集，`SatelliteResourceLanguages`
只保留英文、日文和简体中文框架资源。两个 EXE 仍各自包含完整运行时，第一次启动时会产生
一次解压开销。项目没有启用 `PublishTrimmed`，因为 .NET 8 不支持或不建议对 WPF 程序进行
裁剪，强行裁剪可能破坏 XAML、数据绑定和 JSON 反射访问。

请从仓库根目录执行 Make 目标；脚本会先删除已有的发布目录和 ZIP，避免旧版文件残留。

### 发布 Bootstrapper

```powershell
dotnet publish src/Alls.Bootstrapper/Alls.Bootstrapper.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true
```

### 发布 Configurator

```powershell
dotnet publish src/Alls.Configurator/Alls.Configurator.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true
```

Bootstrapper 的 JSON 和 Assets 是外部内容文件；即使使用完全自包含单文件，也必须与
`ALLS.exe` 一同分发。Configurator 本身可以仅分发单个 EXE。

## 快速开始

1. 构建或下载 Bootstrapper 与 Configurator 的 Windows x64 产物。
2. 将配置器放到 Bootstrapper 部署目录，运行 `ALLS.Configurator.exe`。
3. 打开或新建 `alls-launcher.json`，配置游戏启动命令、监控目标和输入设备。
4. 在“校验与 JSON”页确认没有错误并保存。
5. 先运行 `ALLS.exe --preview --windowed` 检查布局与流程。
6. 确认无误后再使用正常模式启动。

默认配置会查找 `ALLS.exe` 同目录的 `start.bat` 或 `启动.bat`，并等待名为
`Sinmai.exe` 的进程及其可见窗口。不同版本的游戏可执行文件名可能不同，部署前请检查
`games[].launch` 和 `games[].monitor`。

## Bootstrapper 使用方式

命令行参数：

- `--config <path>`：使用指定 JSON 文件；
- `--language zh-CN|en-US|ja-JP`：临时覆盖界面语言；
- `--windowed`：临时覆盖为窗口模式；
- `--preview`：不执行游戏、更新、电源操作或其他命令。

```powershell
ALLS.exe --preview --windowed
ALLS.exe --config D:\ALLS\alls-launcher.json
```

### 机台与键盘操作

| 场景 | 机台按键 | 键盘调试键 |
| --- | --- | --- |
| 启动中进入操作界面 | 1P/2P `SELECT` | `S` 或 `Tab` |
| 启动中跳至最后阶段并启动 | 4 号或 5 号 | `4`/↓ 或 `5`/Enter/Space |
| 操作界面上移 | 1 号 | `1`/↑/小键盘 8 |
| 操作界面下移 | 4 号 | `4`/↓/小键盘 2 |
| 操作界面确认 | 5 号 | `5`/Enter/Space |
| 游戏/操作列表快速切换 | 同时按 2+7 号 | 同时按主键盘 `2`+`7` |
| 退出启动器 | — | `Esc`（需在配置中允许） |

## 运行流程

```text
默认游戏或操作界面
        ↓ 选择游戏
按 sourceIds 尝试自动更新（失败仍继续）
        ↓
播放该游戏的 STEP 时间线（4/5 可跳至 Launch）
        ↓
运行 launch 中的批处理或程序
        ↓
等待 monitor 中真正的游戏进程/窗口
        ↓ 就绪并等待 readyDelayMs
隐藏界面，在后台守护
        ↓ 进程结束或窗口消失
按 onExit 显示操作界面或错误页
```

## 项目结构

```text
ALLS/
├── Directory.Build.props  两个项目共用的单文件发布参数
├── Makefile               Windows/macOS 构建与发布入口
├── scripts/               两个平台的发布和压缩脚本
└── src/
    ├── Alls.Bootstrapper/
    │   ├── Assets/          Logo、加载 GIF 与应用图标
    │   ├── Controls/        GIF 逐帧播放控件
    │   ├── Infrastructure/ 命令行解析
    │   ├── Models/          JSON 配置与启动阶段模型
    │   ├── Resources/       主题和三语言文本
    │   ├── Services/        HID、监控、更新、启动、日志与配置服务
    │   ├── ViewModels/      启动、菜单、更新与错误状态机
    │   ├── App.xaml         应用初始化
    │   └── MainWindow.xaml  启动与操作界面
    └── Alls.Configurator/
        ├── Infrastructure/ 枚举选项和列表文本转换
        ├── Services/        JSON 读写、规范化与配置校验
        ├── App.xaml         配置器初始化
        └── MainWindow.xaml  分区配置界面
```

配置器在编译期链接 Bootstrapper 的配置模型源文件，两者共享同一套字段、枚举和默认值。
原始 C++ 工程保存在 `legacy/native-win32`，仅供追溯，不参与当前解决方案构建。

## 文档

- [JSON 配置参考](docs/configuration.md)
- [Configurator 设计说明](docs/configurator.md)
- [默认配置文件](src/Alls.Bootstrapper/alls-launcher.json)

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
