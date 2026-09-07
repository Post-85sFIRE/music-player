# yunMusicPlayer（云盘音乐播放器）

跨平台云盘音乐播放器 · Windows 端（WPF + .NET 10 + BASS 音频引擎 + 坚果云 WebDAV）。

> 当前为 v0.1.0 早期版本，面向自用与小范围体验。

## 功能
- **本地音乐库**：文件夹扫描、ID3/APE/Vorbis 标签解析、封面提取
- **完整播放**：播放/暂停/上下曲/循环/随机/音量/播放列表（拖拽排序）
- **高品质输出**：WASAPI 共享（默认），可选 WASAPI 独占
- **无损解码**：FLAC / APE / WAV / AIFF / ALAC / MP3 / AAC / OGG / Opus
- **云播放**：坚果云 WebDAV（通用协议，可接任意 WebDAV 源），边播边缓存/下载
- **歌词**：本地 .lrc / 内嵌标签 / 云盘同目录 .lrc / 网络 API（三层来源）

## 下载
- 前往本项目的 **Releases** 页下载 `MusicPlayer-Desktop-win-x64-v0.1.0.zip`
- 解压到任意**可写目录**（如桌面、文档、U 盘），双击 `MusicPlayer.Desktop.exe` 即可运行
- **无需安装 .NET Runtime**（已自包含打包）

## 运行要求
- Windows 10 19041 (20H1) 或更高版本，x64
- 声卡 / 音频输出设备

## 真便携说明
- 本程序为**真便携**：曲库、设置、播放列表、云源配置全部写在 exe 同目录的 `library.db`，
  诊断日志 `diag.log` 也在同目录。把整个目录拷到别的电脑 / U 盘即可原样带走。
- 下载缓存默认在 `音乐库\MusicPlayerCache`，可在设置里改。
- 请勿把程序放在只读目录（如 `C:\Program Files`）下运行，否则数据库无法写入。

## 关于 SmartScreen
未签名程序在别人电脑上首次运行可能被 Windows SmartScreen 拦截，
提示“Windows 已保护你的电脑”。点击“更多信息”→“仍要运行”即可。这是正常提示，程序不含任何恶意行为。

## 音频引擎授权
解码使用 [BASS](https://www.un4seen.com/) 音频库（Un4seen，非商业用途免费）。
本项目仅作个人 / 小范围使用，不涉及任何曲库分享或搜索他人资源功能。

## 构建（开发者）
```bash
dotnet publish src/MusicPlayer.Desktop/MusicPlayer.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```
原生 BASS dll 位于 `src/MusicPlayer.Audio/Native/x64/`，由构建复制到输出目录。
（如原生解码插件缺失，请在本机以 PowerShell 运行 `tools/fetch-bass.ps1`。）

## 版权与合规
- 仅播放 / 下载**用户自有**的文件，属于个人使用范畴。
- 不内置任何“搜索 / 分享他人资源”功能。
