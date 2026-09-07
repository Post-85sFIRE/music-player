# MusicPlayer v0.1.0

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
