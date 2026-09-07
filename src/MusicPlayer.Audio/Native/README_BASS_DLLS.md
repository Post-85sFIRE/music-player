# BASS 原生 DLL 部署说明

BASS 引擎依赖 Un4seen 提供的原生 `bass*.dll`。NuGet 的 `Un4seen.Bass` 包**只随附托管封装
`Bass.Net.dll`**（构建后出现在输出目录），**并不包含**原生 `bass.dll`。原生 `bass*.dll`
（核心 + 解码插件 + WASAPI 插件）都需从 un4seen.com 下载后手动放到输出目录。

## 一键获取（推荐，在你自己的 Windows 上运行）

开发环境（沙箱）出网受限，无法预先代下载原生 dll，因此请在本机执行脚本，自动从
un4seen.com 拉取并放入 exe 目录：

```powershell
# 方式 A：先切到仓库根目录，再运行
#（PowerShell 默认不会在当前目录搜索脚本，必须写 .\）
cd "D:\我的文件\Documents\WorkBuddy\音乐播放器"
.\tools\fetch-bass.ps1

# 方式 B：从任意位置用完整路径运行（脚本内部会自动定位仓库根目录）
powershell -ExecutionPolicy Bypass -File "D:\我的文件\Documents\WorkBuddy\音乐播放器\tools\fetch-bass.ps1"
```

> 如果提示"无法加载，因为在此系统上禁止运行脚本"，先执行一次：
> ```powershell
> Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser -Force
> ```

脚本会下载 x64 版 `bass.dll / basswasapi.dll / bassflac.dll / bassopus.dll / bassape.dll / bassalac.dll`
（`basswasapi.dll` 为 M1.x 独占输出所需；缺它时自动回退共享输出，不影响出声）
到两处：
- `src/MusicPlayer.Audio/Native/x64/`（暂存，已配置 `<None CopyToOutputDirectory>` 随 `dotnet build` 自动拷贝）
- `src/MusicPlayer.Desktop/bin/<Config>/<Tfm>/`（输出目录，可直接运行）

执行后即可双击 `MusicPlayer.Desktop.exe` 验证播放。若缺少 dll，程序启动时也会弹出中文提示指引你运行该脚本。

## 需要的文件（放到 MusicPlayer.Desktop 生成目录，即 exe 同目录）

从 https://www.un4seen.com/ 下载 BASS 及其 add-on（非商业免费），解压后取：

- `bass.dll`           —— 核心（MP3/WAV/OGG 等基础解码 + 输出）
- `bassflac.dll`       —— FLAC
- `bassopus.dll`       —— Opus
- `bassape.dll`        —— APE
- `bassalac.dll`       —— ALAC
- `basswasapi.dll`     —— WASAPI 独占输出（M1.x 启用独占绑定需要）
- `bassfx.dll`         —— 均衡器/淡入淡出 DSP（后续增强）

> 说明：OGG/Vorbis 已由 `bass.dll` 原生支持，无需单独的 `bass_ogg.dll`；
> AAC（商业插件 `bass_aac`）不在本脚本下载范围内，M1 暂不支持 AAC 源文件。

## 授权

非商业用途免费；商业化须在 https://www.un4seen.com/ 购买授权，并在"关于"中保留 BASS 版权声明。

## 进程架构

本解决方案仅编译 **x64**（`.csproj` 已设 `<PlatformTarget>x64>`），
请放置对应 x64 版本的原生 dll。
