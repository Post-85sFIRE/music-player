# MusicPlayer —— 本地音乐播放器（第一期 MVP）

Windows 桌面端（WPF + C# + .NET 10 + BASS），参考酷狗的通用播放功能，强调高音质。
完整方案见 `..\可行性分析.md`，实施蓝图见计划文件（`C:\Users\Piggy\.workbuddy\plans\swift-aurora-newton-s2njDHMm.md`）。

## 已完成（M1 范围）
- 分层架构：Core（模型/接口）/ Audio（BASS 引擎）/ Data（SQLite + Dapper）/ Services（播放编排、扫描、设置、空间统计）/ Desktop（WPF UI）。
- 通用播放：播放/暂停/停止/上一首/下一首、顺序/单曲循环/列表循环/不重复随机。
- 音量、进度、播放模式切换。
- 本地音乐库：文件夹扫描 + TagLib 标签建库 + 封面提取 + 去重 + FileSystemWatcher 增量监听。
- 播放列表：添加（文件/文件夹）、删除、搜索、拖拽排序、持久化。
- 设置：WASAPI 独占开关（探测+回退）、下载/缓存目录、后台空间统计。
- 云源/下载缓存接口已预留（`ICloudSourceProvider` / `IDownloadCacheService`），第二期无需改 UI 与播放内核。

## 构建与运行
需要 .NET 10 SDK（https://dot.net）。在 `src` 目录：

```
dotnet new sln -n MusicPlayer
dotnet sln add MusicPlayer.Core MusicPlayer.Audio MusicPlayer.Data MusicPlayer.Services MusicPlayer.Desktop MusicPlayer.Tests
dotnet build -c Release
dotnet run --project MusicPlayer.Desktop
```

（也可在 IDE 中打开 `MusicPlayer.sln` 生成并运行。）

## BASS 原生 DLL（必须）
BASS 依赖 Un4seen 的原生 `bass*.dll`，需放到 `MusicPlayer.Desktop` 的输出目录（exe 同目录）：
`bass.dll / bassflac.dll / bassopus.dll / bassape.dll / bassalac.dll / bass_ogg.dll / bassaac.dll`
以及独占输出 `basswasapi.dll`、DSP `bassfx.dll`。
详见 `MusicPlayer.Audio/Native/README_BASS_DLLS.md`。本仓库不内置这些二进制（许可要求随官方分发）。
非商业免费；商业化须向 Un4seen 购买授权并在"关于"保留版权声明。

## 已知限制（M1）
- WASAPI **独占输出绑定**为探测+回退基线：M1 默认走 BASS 标准输出（Windows 即 WASAPI 共享），
  完整独占流绑定（`BASS_WASAPI_StreamCreate` + `BASS_WASAPI_Start`）作为 M1.x 的立即后续项，已在 `BassRuntime.TryReserveWasapiExclusive` 标注 TODO。
- 鸿蒙端（第三期）与云盘（坚果云 WebDAV，第二期）已实现，接口已预留。
