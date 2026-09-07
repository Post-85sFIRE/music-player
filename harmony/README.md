# 音乐播放器 · HarmonyOS 端（ArkTS / Stage 模型）

本目录是四期路线中的 **M3（鸿蒙手机端）** 源码，与 Windows 端（WPF/C#）功能对齐：

- 本地音乐：通过 `AudioViewPicker` 免授权选取音频文件，`AVPlayer` 状态机播放（播放/暂停/上下曲/进度/音量）。
- 播放模式：顺序 / 单曲循环 / 随机。
- 云盘：WebDAV（坚果云）连接、PROPFIND 列目录、GET 流式缓存到沙箱后由 `AVPlayer` 播放；设计对齐 Windows 端 `WebDavCloudProvider` + `DownloadCacheService`。
- 设置持久化：`@ohos.data.preferences`（音量、播放模式、云盘配置、播放列表、网络歌词开关、ReplayGain 模式）。
- 云盘密码：**设备级加密**（`@ohos.security.asset`），不明文落盘。

## 工程结构
```
harmony/
  AppScope/app.json5
  build-profile.json5
  oh-package.json5
  entry/src/main/
    module.json5                 # 含 ohos.permission.INTERNET（云播放需要）
    ets/
      entryability/EntryAbility.ts
      model/
        Track.ts                 # 曲目模型 + 播放列表 DTO 互转 (toDto/fromDto) + isCached
        Settings.ts              # AppSettings / CloudConfig / PlaylistDto + SettingsStore（密码走 Asset）
        SecureStore.ts           # @ohos.security.asset 封装（saveSecret/getSecret/deleteSecret）
        PlayerEngine.ts          # AVPlayer 状态机封装
        LibraryScanner.ts        # AudioViewPicker 选曲
        PlaylistManager.ts       # 队列：增删/上下曲/拖拽排序/清空/持久化
        WebDavClient.ts          # WebDAV：PROPFIND 列目录 + GET 流式缓存 + getText + isCached
        Lyrics.ts                # LRC 解析（多时间戳/二分定位）
        LyricsService.ts         # 三层来源：本地 .lrc → 云盘 .lrc → LRCLIB → api.lyrics.ovh
      viewmodel/MainViewModel.ts # 编排：自动重连/已缓存/歌词/ReplayGain/列表持久化/设置接口
      pages/Index.ets            # 主界面（播放控制 + 音乐库 + 云盘 + 右侧歌词面板）
      pages/SettingsPage.ets     # 设置页（云盘增删/重连 + 音量/模式/网络歌词/ReplayGain）
    resources/                  # string / color / 图标 / main_pages（已注册 Index + SettingsPage）
```

## 二期（M3 增量）已实现能力
> 与 Windows 端第二阶段（M1）对齐。代码在本沙箱已写完并通过静态自审，**仍需在本机 DevEco Studio 编译验证**（沙箱无 HarmonyOS SDK）。

- **P0-1 启动自动重连 + 密码设备级加密**
  - 启动时 `MainViewModel.init()` 用已存凭据（密码从 Asset 读取）自动重建 `WebDavClient` 并 `login()`，结果写状态栏（`已自动连接 N 个云盘` / `自动重连失败：…`）。
  - 云盘密码不明文落盘：`SettingsStore` 仅把非敏感字段写 preferences，密码按 `musicplayer_cloud_pwd_<id>` 存 Asset。
- **P0-2 / P0-3 云盘与主列表"✓已缓存"标记**
  - 云盘列表项、主播放列表云曲均显示绿色 `✓已缓存`（基于 `WebDavClient.isCached` / `cacheDir/webdav/<safeName>.cache` 存在且 size>0）。
- **P1 歌词（三层来源 + 解析 + 面板 + 搜索 + 高亮）**
  - 优先级：本地同目录 `.lrc`（文件名匹配）→ 云盘同目录 `.lrc`（GET）→ LRCLIB（中文优先）→ api.lyrics.ovh；默认开启网络歌词。
  - 主界面右侧歌词面板：搜索框、当前行蓝色高亮、点击歌词行 `seek`、随播放进度自动滚动。
  - 鸿蒙无 TagLib，**不支持内嵌标签解析**，仅解析 `.lrc` 文本（与 Windows 端"内嵌标签"来源的差异，已在 LyricsService 注释标注）。
- **P1-7 独立设置页**
  - `SettingsPage.ets`：云盘源增删 / 账号 / 密码（掩码）/ 重连、音量、播放模式、网络歌词开关、ReplayGain 模式；经 `router.pushUrl` 从主界面打开。
- **P1 播放列表增强**
  - 拖拽排序（`onDragStart`/`onDrop` → `replay(reorder)`）、`移除选中` / `清空列表`、`playTrack` 高亮当前项；列表跨重启持久化（`PlaylistManager.toDtos`/`restore` → preferences）。
- **P2 ReplayGain 模式**
  - 关 / 专辑(-3dB, 系数 0.708) / 单曲(-6dB, 系数 0.5)；鸿蒙无 TagLib，以"全局音量系数近似"实现（增益乘到 `setVolume`），已在代码注释标注与 Windows 端精确 RG 的差异。


## 如何编译与运行
> ⚠️ 本代码**无法在当前开发沙箱（Windows + .NET 工具链）中编译**——HarmonyOS 需要 DevEco Studio 与 HarmonyOS SDK（API 12），本机没有该工具链。请在你的鸿蒙开发机上按以下步骤操作。

1. 安装 **DevEco Studio 5.0+**，下载 **HarmonyOS SDK API 12（HarmonyOS 5.0）**。
2. 用 DevEco Studio 打开本 `harmony/` 目录（识别为 Stage 模型工程）。
3. 用真机或模拟器（phone）运行 `entry` 模块；首次运行按提示完成签名（build-profile 的 `signingConfigs` 留空，由 DevEco 自动生成调试证书）。
4. 权限：仅 `INTERNET` 在 `module.json5` 声明；本地音乐走 `AudioViewPicker` 免授权，无需存储/媒体库权限。

## 关键 API（已对照官方文档）
- `media.createAVPlayer()`（`@kit.MediaKit`）：状态机 `idle→initialized→prepared→playing→paused→completed`；设 `url` 后 `prepare()`，再 `play()`。
- `picker.AudioViewPicker`（`@ohos.file.picker`）：返回 `file://` URI 数组，直接作为 `AVPlayer.url`。
- `http.createHttp()` + `customMethod: 'PROPFIND'`（`@kit.NetworkKit`）：列目录；流式 `dataReceive` 写沙箱缓存。
- `preferences`（`@ohos.data.preferences`）：非敏感设置 / 播放列表持久化。
- `security.asset`（`@ohos.security.asset`）：云盘密码设备级加密（无需额外权限声明，按 alias 存/取）。

## 已知限制 / 待补
- 云盘仅实现 WebDAV（坚果云），通用 WebDAV 协议，可接任意 WebDAV 源。
- 沙箱图标为 1×1 占位 PNG，正式发布请替换 `resources/base/media/icon.png`、`startIcon.png` 为正式图标。
- 因无法在沙箱实机联调，部分 API 调用形式（如 `AVPlayer.url` 对 picker URI、`fs` 流式写入、`Asset` 读写）建议在 DevEco 中首次运行后按需微调。
- 歌词仅支持 `.lrc` 文本（无 TagLib 内嵌标签解析）；ReplayGain 为全局音量系数近似（非精确 RG）。
- 二期代码**未经真机/模拟器编译**，请在本机 DevEco Studio 完成首编与联调。

## DevEco 首编 / 真机联调排查清单
> 沙箱无 HarmonyOS SDK，下列是**本机首编/首跑时必须逐一确认**的点，按"编译期 → 运行期 → 重点风险"排序。每条都对应本项目已写代码里的具体位置。

### A. 工具链与签名
1. DevEco Studio 5.0+，SDK 选 **API 12（HarmonyOS 5.0）**；`File → Project Structure` 确认 `compileSdkVersion` / `compatibleSdkVersion` 为 12。
2. 真机：开发者选项开 **USB 调试** + **"允许 ADB 调试"**；首次运行 DevEco 自动生成调试证书（build-profile 的 `signingConfigs` 留空即可）。模拟器选 phone 镜像（需本地已下载对应系统镜像）。
3. 若报 `hvigor` / `node` 版本不符：用 DevEco 自带 Node（不要系统 Node），或按提示装匹配版本。

### B. 编译期（ArkTS 类型 / API 形态）逐项核对
4. `Index.ets` `onDragStart` 返回空 `DragItemInfo`（`return {}`）。若编译器要求 `data` 字段且 `data` 为 `object` 类型不接受 string，保持 `{}` 最稳；若它要求 `pixelMap`，补 `pixelMap: undefined`。
5. `Index.ets` `onDrop(() => {...})` 用**零参箭头**，避免依赖全局 `DragEvent` 类型。若首编报 `DragEvent` 找不到，改为 `import { DragEvent } from '@kit.ArkUI';` 并把参数写为 `(_e: DragEvent, _x: string) => {...}`。
6. `Select([{value:'顺序'}, ...])` 的 `.selected(number)` / `.onSelect((i:number)=>...)`（API 12 签名）。
7. `Toggle({ type: ToggleType.Switch, isOn })` —— `ToggleType` 已从 `@kit.ArkUI` 导入；确认 `isOn` 接受 `boolean` 状态。
8. `Scroller.scrollToIndex(index, true)` 平滑滚动；`scrollToIndex(0)` 无动画也合法。
9. `router.pushUrl({ url: 'pages/SettingsPage' })` / `router.back()` —— `router` 已从 `@kit.ArkUI` 导入；目标页已注册在 `main_pages.json`。
10. `ForEach` key 生成器：library / cloud 用 `t.id` / `e.id`，歌词用 `index.toString()`（歌词列表不排序，索引稳定）。不要对会重排的列表用 index 当 key。
11. `Blank().layoutWeight(1)` 用于把 `⚙设置` 按钮推到最右；`Row().wrapContent(true)` 让云盘源按钮换行。若 SDK 版本不支持 `wrapContent`，删掉该调用（按钮可能溢出，不影响编译）。

### C. 权限与能力（module.json5）
12. 已声明 `ohos.permission.INTERNET`（云播放 + 歌词网络源 LRCLIB / api.lyrics.ovh 必需）。若首编/运行报权限缺失，检查 `requestPermissions` 是否仍在 `entry` 的 `module` 下。
13. Asset（`@ohos.security.asset`）：通常**无需额外权限**；若设备要求持久化到非易失存储，按报错补 `ohos.permission.STORE_PERSISTENT_DATA`（加到 `requestPermissions`）。
14. 本地音乐走 `AudioViewPicker` 免授权，无需 `READ_MEDIA` / 存储权限。

### D. 运行期联调（沙箱无法测，本机必验）
15. **【重点① fs 同步/异步】** `WebDavClient.ts` 里 `if (fs.access(localPath))` 与 `fs.stat(localPath)` 用的是**同步**形态。请首编确认 `@ohos.file.fs` 的 `access`/`stat` 在当前 SDK 是同步方法；若只有 `Promise` 版（`access(path): Promise<boolean>`），需改成 `await fs.access(...)` 并把 `cacheAndGetLocal`/`isCached` 的同步段重写为 async。这是最可能导致"启动后云歌点不开/不缓存"的坑。
16. **【重点② Asset 字段常量】** `SecureStore.ts` 用 `asset.Asset.save/query/delete` 的 query 结构（含 `Asset.Tag.SECRET` / `Asset.Tag.ALIAS` / `Asset.Tag.SYNC_TYPE` 等）。首编核对常量名与枚举归属（不同 API 版本常量名略有差异）；若存/取失败，密码会回退为空 → 云盘重连失败（状态栏显示"自动重连失败"）。
17. **【重点③ 启动卡顿】** `EntryAbility.onCreate` 里 `await vm.init()` 内含 `reconnectCloud()`（对每个云盘 `await login()`）。无网/云盘慢时会**阻塞启动**直到重连超时。若首启明显慢，可把重连改为 fire-and-forget（`vm.init()` 内不 await 重连，先 `loadContent` 再后台重连并回调刷新状态栏）。
18. `AVPlayer.url` 对 picker 返回的 `file://` URI：官方支持直接播放；若某格式（如 ape/dsf）无声，确认 AVPlayer 解码支持范围（或退回常见 mp3/flac 验证链路）。
19. 歌词网络源需设备联网；模拟器要在 **Settings → WLAN** 配网或用本机代理。LRCLIB 中文优先、api.lyrics.ovh 偏西文，无结果时面板显示"未找到歌词"（非 bug）。
20. 缓存键一致性：`WebDavClient.safeName(entryId)` 同时用于 `cacheAndGetLocal` 落盘与 `isCached` 查询（均在 client 内），一致；`✓已缓存` 标记依赖它，二者必同步。

### E. 日志定位
21. `EntryAbility.ts` 已 `import { hilog }`，tag 为 `MusicPlayer`。DevEco **Log** 窗口过滤 `MusicPlayer` 看启动/重连/报错；`PlayerEngine.onError` 会把 AVPlayer 错误码写进 `vm.status`（主界面顶部状态栏可见）。

### F. 常见问题速查
- 启动白屏/卡住 → 看 E. 重点③，或 Log 里 `vm.init` 是否抛错。
- 云歌点不开、状态栏"自动重连失败" → 重点②（密码未从 Asset 读出）；SettingsPage 里 `重连` 重试，或删掉云盘重加。
- 云歌能连但播放没声音/不缓存 → 重点①（fs 同步形态）。
- 歌词面板空 → 该曲确无 .lrc / 云 .lrc / 网络未匹配（中文歌 LRCLIB 覆盖有限，属正常）。
- 拖拽排序无效 → 重点④/⑤（onDragStart/onDrop 编译通过但需真机手势验证）。

## 与 Windows 端对齐
| 能力 | Windows (WPF) | HarmonyOS (ArkTS) |
|------|---------------|-------------------|
| 本地播放 | BASS + WASAPI 独占 | AVPlayer |
| 扫描/选取 | 目录扫描 TagLib | AudioViewPicker |
| 云盘 | WebDAV（坚果云） | WebDAV（坚果云） |
| 缓存 | DownloadCacheService | cacheDir 流式缓存 |
| 已缓存标记 | ✓ | ✓（✓已缓存） |
| 启动自动重连 | ✓ | ✓（init 自动重建 + Asset 密码） |
| 歌词 | 内嵌标签 + 本地 + 云 + 网络 | .lrc 文本 + 云 .lrc + 网络（无内嵌标签） |
| 设置 | JSON 文件 | preferences + Asset（密码） |
| 播放列表持久化 | ✓（SQLite） | ✓（preferences JSON） |
| ReplayGain | 精确 RG（计划） | 全局音量系数近似（P2） |


## DevEco 6.1 打开工程报 “Cannot Open Project” 的修复（2026-09-06 已补）

症状：DevEco Studio 6.1 打开 `harmony/` 报 “Select an OpenHarmony or HarmonyOS project”，拒绝识别为有效工程。

根因：原工程是极简（API 9–11 风格）骨架，缺少 HarmonyOS NEXT（API 12）工程必须的 Hvigor 脚手架。DevEco 6.1 打开工程时会校验这些文件，缺失即判定“非有效工程”。

已补齐的文件（均在 `harmony/` 下）：
- `hvigor/hvigor-config.json5` — 含 `modelVersion: "6.0.2"`（DevEco 6.1 支持 5.0.0–6.0.2 区间，两处 modelVersion 须一致）
- 根 `hvigorfile.ts` 与 `entry/hvigorfile.ts` — 引用 `@ohos/hvigor-ohos-plugin`（appTasks / hapTasks）
- `entry/build-profile.json5`、`entry/oh-package.json5` — 模块级清单
- 根 `oh-package.json5` — 增加 `modelVersion` 且 devDependencies 含 `@ohos/hvigor`、`@ohos/hvigor-ohos-plugin`（均 6.0.2）
- `build-profile.json5` — 改为 NEXT 格式：`products` 内 `compileSdkVersion` / `compatibleSdkVersion` / `targetSdkVersion` 均为字符串 `"5.0.0(12)"`，`runtimeOS: "HarmonyOS"` 置于 `products` 下

打开后处理：
- 若 DevEco 弹 “hvigor 版本不匹配 / Migrate”，点 Sync 或按提示 Migrate，IDE 会把 modelVersion 与 hvigor 依赖对齐到其内置版本（可恢复，不必手改）。
- API 12 SDK 必须已安装（File → Settings → SDK → HarmonyOS → API 12），否则报缺 SDK。
- 首次打开会触发 ohpm install 拉取 `@ohos/hvigor` 等依赖，需联网（华为 npm 源）。
