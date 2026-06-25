# MusicPlayer

一个基于 WPF 的本地音乐播放器，支持歌单管理、音频播放、播放进度控制、主题切换和系统托盘控制。

## 功能特性

- 播放本地音频文件，支持 `mp3`、`wav`、`flac`、`aac`、`wma`、`m4a` 等格式
- 使用 NAudio 负责音频播放，使用 TagLibSharp 读取歌曲标题、歌手、专辑和时长等元数据
- 支持创建、重命名、删除、拖拽排序歌单
- 支持向歌单添加音乐、移除歌曲、拖拽调整歌曲顺序、打开歌曲所在位置
- 支持歌单封面设置
- 支持顺序循环和随机循环播放模式
- 支持播放、暂停、上一首、下一首、进度拖动、音量调节和静音
- 支持深色、浅色和跟随系统主题
- 支持关闭到系统托盘，并通过托盘菜单控制播放
- 支持单实例运行，重复启动时会唤起已有窗口
- 自动保存窗口状态、音量、主题、歌单、当前播放歌曲和播放进度

## 技术栈

- C# / WPF
- .NET `net10.0-windows`
- NAudio `2.3.0`
- TagLibSharp `2.3.0`

## 运行要求

- Windows
- .NET 10 SDK
- Visual Studio 2026 或支持 .NET 10 / WPF 的 IDE

如果本机尚未安装 .NET 10 SDK，可以在 [MusicPlayer.csproj](E:/WPF_Projects/MusicPlayer/MusicPlayer/MusicPlayer.csproj) 中将 `TargetFramework` 调整为本机已安装且支持 WPF 的 Windows 目标框架，例如 `net9.0-windows` 或 `net8.0-windows`。

## 构建与运行

在仓库根目录执行：

```powershell
dotnet restore
dotnet build .\MusicPlayer.slnx
dotnet run --project .\MusicPlayer\MusicPlayer.csproj
```

发布 Release 版本：

```powershell
dotnet publish .\MusicPlayer\MusicPlayer.csproj -c Release
```

## 发布后创建搜索入口

仓库根目录提供了 [install-start-menu-shortcut.bat](E:/WPF_Projects/MusicPlayer/install-start-menu-shortcut.bat)，用于给已发布的 `MusicPlayer.exe` 创建当前用户的开始菜单快捷方式。

发布时可以采用以下任一目录结构：

```text
MusicPlayer_win-x64
├── MusicPlayer.exe
├── MusicPlayer.dll
├── MusicPlayer.deps.json
└── ...
```

将 `install-start-menu-shortcut.bat` 放进 `MusicPlayer_win-x64` 目录后运行。

也可以把脚本放在发布目录旁边：

```text
.
├── install-start-menu-shortcut.bat
└── MusicPlayer_win-x64
    ├── MusicPlayer.exe
    ├── MusicPlayer.dll
    ├── MusicPlayer.deps.json
    └── ...
```

双击运行脚本后，会创建：

```text
%APPDATA%\Microsoft\Windows\Start Menu\Programs\MusicPlayer.lnk
```

创建成功后，Windows 搜索、PowerToys Run、Flow Launcher、Listary 等启动器通常都可以通过 `MusicPlayer` 搜索到应用。

## 数据与日志

应用状态会保存到当前用户的 AppData 目录：

```text
%APPDATA%\MusicPlayer\app-state.json
```

性能日志和异常日志会写入：

```text
%APPDATA%\MusicPlayer\logs\perf.log
```

这些文件属于本地运行数据，不会提交到仓库。

## 项目结构

```text
.
├── MusicPlayer.slnx
├── Readme.MD
├── .gitignore
└── MusicPlayer
    ├── App.xaml / App.xaml.cs
    ├── MainWindow.xaml / MainWindow.xaml.cs
    ├── AppStorage.cs
    ├── AppTheme.cs
    ├── AppUi.cs
    ├── PerfLog.cs
    ├── TrayMenuWindow.xaml / TrayMenuWindow.xaml.cs
    ├── ConfirmDialog.xaml / ConfirmDialog.xaml.cs
    ├── Views
    │   ├── HomeView.xaml
    │   ├── PlaylistContentView.xaml
    │   ├── NowPlayingView.xaml
    │   ├── LibraryView.xaml
    │   └── SettingsView.xaml
    └── Assets
        └── Icons
```

## 说明

当前项目主要面向本地音乐播放和歌单管理。歌单中保存的是本地音频文件路径，如果移动或删除原始音乐文件，应用将无法继续播放对应曲目，需要重新添加文件。
