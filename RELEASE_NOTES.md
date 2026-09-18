# MusicPlayer 发布说明

---

# MusicPlayer v1.0.0（Windows）

首个正式版。Windows x64 自包含便携版（无需安装 .NET Runtime）。

发布日期：2026-09-11 落地

## 下载

| 平台 | 文件 | 大小 | SHA256 |
| --- | --- | --- | --- |
| Windows x64 | `MusicPlayer-Desktop-win-x64-v1.0.0.zip` | 84,001,031 字节（约 80.11 MB） | `5A4C20E22CDEBC07FFECE7E22C97591DD170DDD28CF52A25395ECE6A7A745E59` |
| Android | `app-yunMusicPlayer-release.apk` | 16,960,446 字节（约 16.17 MB） | `978304f99ebea022711a664e09abb67ff83e9f65cc2ae289e0693e31f89e3c8d` |

> Android 端首次发布。应用内已内置版本更新（拉取仓库根目录 `version.json`），
> 启动自动检查 + 设置页手动检查；下载地址即上方 Android 行的 Release 资源。

## 更新说明（Windows）

- 修复云盘歌曲播放报 “400 Bad Request”（WebDAV 完整 URL 被重复拼接 + 双重编码）
- 修复“云源未连接”（`Track.SourceId` 曾误存为云盘文件相对路径，导致按 sourceId 查不到 provider）
- 双击未缓存云曲改为后台静默加载：不弹窗、不打断当前播放，加载失败自动重试
- 歌词自动加载默认取第一条候选，不再弹选择框；仅手动搜索才弹候选窗
- 播放列表工具栏的云盘同步提示改为独立一行，不再被按钮挤掉
- 更换应用图标（古风飘带音符）

## 使用

1. 下载 `MusicPlayer-Desktop-win-x64-v1.0.0.zip`
2. 解压到可写目录，双击 `MusicPlayer.Desktop.exe`
3. 首次运行曲库为空，请在“设置 / 资料库”扫描你的音乐文件夹

## 运行要求

Windows 10 19041+ x64，需音频输出设备。

## 已知事项

- 安装包未签名，首次运行可能被 SmartScreen 拦截（点“更多信息 → 仍要运行”）
- 数据为真便携，写在 exe 同目录 `library.db`
- 仅支持用户自有文件，不含任何资源搜索 / 分享功能

---

# MusicPlayer v0.1.0（历史版本）

首个公开发布版本（早期预览，面向自用与小范围体验）。

## 包含的版本

- Windows x64 自包含便携版（无需安装 .NET Runtime）

## 功能

- 本地音乐库扫描与标签解析（FLAC / APE / WAV / AIFF / ALAC / MP3 / AAC / OGG / Opus）
- 完整播放控制与播放列表（拖拽排序、循环、随机）
- WASAPI 共享输出（默认）；设置中可开启 WASAPI 独占
- 坚果云 WebDAV 云播放（边播边缓存 / 下载）
- 三层来源歌词（本地 .lrc / 内嵌 / 云盘同目录 / 网络）

## 使用

1. 下载 `MusicPlayer-Desktop-win-x64-v0.1.0.zip`
2. 解压到可写目录，双击 `MusicPlayer.Desktop.exe`
3. 首次运行曲库为空，请在“设置 / 资料库”扫描你的音乐文件夹

## 已知事项

- 安装包未签名，首次运行可能被 SmartScreen 拦截（点“更多信息 → 仍要运行”）
- 数据为真便携，写在 exe 同目录 `library.db`
- 仅支持用户自有文件，不含任何资源搜索 / 分享功能

## 运行要求

Windows 10 19041+ x64，需音频输出设备。
