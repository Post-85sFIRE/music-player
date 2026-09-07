// 首页：沉浸式"正在播放"页（参考图风格：大封面 + 居中歌名/歌手 + 简洁进度条 + 大播放按钮）。
// 播放列表改为底部抽屉弹出；云盘/歌词管理仍在 LibraryPage；设置/云盘/关于入口在 MorePage。
// UI 通过 @State 镜像 MainViewModel 的播放态回调；route 回退时 onPageShow 重新同步。
import { MainViewModel } from '../viewmodel/MainViewModel';
import { Track, SourceType } from '../model/Track';
import { PlayMode } from '../model/PlayerEngine';
import { LyricLine } from '../model/Lyrics';
import { SettingsStore } from '../model/Settings';
import { router } from '@kit.ArkUI';
import { common } from '@kit.AbilityKit';

@Entry
@Component
struct Index {
  // VM 来自 AppStorage（EntryAbility 已初始化），此处兜底用 getContext 构造默认实例。
  vm: MainViewModel = new MainViewModel(getContext(this) as common.UIAbilityContext, new SettingsStore());

  @State library: Track[] = [];
  @State currentTitle: string = '未播放';
  @State currentArtist: string = '';
  @State currentId: string = '';
  @State isPlaying: boolean = false;
  @State positionMs: number = 0;
  @State durationMs: number = 0;
  @State mode: number = 0;

  // 当前歌词行（播放页只显示一行，完整面板在音乐库页）
  @State lyricsLines: LyricLine[] = [];
  @State activeLyricIndex: number = -1;

  // 播放列表抽屉
  @State showPlaylist: boolean = false;
  @State playlistTab: number = 0;

  aboutToAppear(): void {
    const stored = AppStorage.get('vm') as MainViewModel;
    if (stored) {
      this.vm = stored;
    }
    this.vm.onLibraryChanged = () => { this.library = this.vm.library.slice(); };
    this.vm.onCurrentChanged = () => {
      this.currentTitle = this.vm.current ? this.vm.current.title : '未播放';
      this.currentArtist = this.vm.current ? this.vm.current.artist : '';
      this.currentId = this.vm.current ? this.vm.current.id : '';
    };
    this.vm.onPlayStateChanged = () => { this.isPlaying = this.vm.isPlaying; };
    this.vm.onProgress = () => {
      this.positionMs = this.vm.positionMs;
      this.durationMs = this.vm.durationMs;
      this.syncActiveLyric();
    };
    this.vm.onLyricsChanged = () => {
      this.lyricsLines = this.vm.lyricsLines.slice();
      this.activeLyricIndex = -1;
    };
    this.vm.onSettingsChanged = () => {
      this.mode = this.vm.playMode as number;
    };
    this.mirrorPlayback();
  }

  onPageShow(): void {
    // 从其他页面返回时重新同步播放态
    this.mirrorPlayback();
  }

  private mirrorPlayback(): void {
    this.library = this.vm.library.slice();
    this.currentTitle = this.vm.current ? this.vm.current.title : '未播放';
    this.currentArtist = this.vm.current ? this.vm.current.artist : '';
    this.currentId = this.vm.current ? this.vm.current.id : '';
    this.isPlaying = this.vm.isPlaying;
    this.positionMs = this.vm.positionMs;
    this.durationMs = this.vm.durationMs;
    this.mode = this.vm.playMode as number;
    this.lyricsLines = this.vm.lyricsLines.slice();
  }

  private syncActiveLyric(): void {
    const idx: number = this.currentLyricIndex(this.positionMs);
    if (idx !== this.activeLyricIndex) {
      this.activeLyricIndex = idx;
    }
  }

  // 二分查找当前应高亮的时间轴行（最后一个 timeMs <= positionMs 的行）
  private currentLyricIndex(ms: number): number {
    const lines: LyricLine[] = this.lyricsLines;
    if (lines.length === 0) {
      return -1;
    }
    let lo: number = 0;
    let hi: number = lines.length - 1;
    let ans: number = -1;
    while (lo <= hi) {
      const mid: number = (lo + hi) >> 1;
      if (lines[mid].timeMs <= ms) {
        ans = mid;
        lo = mid + 1;
      } else {
        hi = mid - 1;
      }
    }
    return ans;
  }

  private formatTime(ms: number): string {
    const totalSec: number = Math.floor(ms / 1000);
    const m: number = Math.floor(totalSec / 60);
    const s: number = totalSec % 60;
    return (m < 10 ? '0' + m : '' + m) + ':' + (s < 10 ? '0' + s : '' + s);
  }

  private modeIcon(): string {
    if (this.mode === PlayMode.LoopOne) {
      return '🔂';
    }
    if (this.mode === PlayMode.Shuffle) {
      return '🔀';
    }
    return '➡';
  }

  private cycleMode(): void {
    const next: number = (this.mode + 1) % 3;
    this.vm.setMode(next as PlayMode);
    this.mode = next;
  }

  @Builder
  PlaylistDrawer() {
    Stack() {
      // 遮罩：点击关闭
      Column()
        .width('100%')
        .height('100%')
        .backgroundColor('#00000055')
        .onClick(() => { this.showPlaylist = false; })

      // 抽屉面板
      Column() {
        // 标题栏
        Row() {
          Text('播放列表').fontSize(18).fontWeight(FontWeight.Bold).fontColor('#1a1a1a')
          Blank().layoutWeight(1)
          Button({ type: ButtonType.Circle }) {
            Text('✕').fontSize(16).fontColor('#666666')
          }
          .width(32).height(32).backgroundColor('#f0f0f0')
          .onClick(() => { this.showPlaylist = false; })
        }
        .width('100%')
        .padding({ left: 16, right: 12, top: 16, bottom: 10 })

        // 标签栏
        Row() {
          Column() {
            Text(`当前列表 ${this.library.length}`)
              .fontSize(14)
              .fontWeight(this.playlistTab === 0 ? FontWeight.Bold : FontWeight.Normal)
              .fontColor(this.playlistTab === 0 ? '#1a1a1a' : '#888888')
            if (this.playlistTab === 0) {
              Divider().color('#5b6ef5').strokeWidth(2).width('80%').margin({ top: 4 })
            }
          }
          .layoutWeight(1)
          .alignItems(HorizontalAlign.Center)
          .onClick(() => { this.playlistTab = 0; })

          Column() {
            Text('历史列表 0')
              .fontSize(14)
              .fontWeight(this.playlistTab === 1 ? FontWeight.Bold : FontWeight.Normal)
              .fontColor(this.playlistTab === 1 ? '#1a1a1a' : '#888888')
            if (this.playlistTab === 1) {
              Divider().color('#5b6ef5').strokeWidth(2).width('80%').margin({ top: 4 })
            }
          }
          .layoutWeight(1)
          .alignItems(HorizontalAlign.Center)
          .onClick(() => { this.playlistTab = 1; })
        }
        .width('100%')
        .padding({ left: 16, right: 16, bottom: 8 })

        // 列表内容
        if (this.playlistTab === 0) {
          if (this.library.length === 0) {
            Text('播放列表为空')
              .fontSize(14)
              .fontColor('#888888')
              .width('100%')
              .textAlign(TextAlign.Center)
              .padding({ top: 40, bottom: 40 })
          } else {
            List() {
              ForEach(this.library, (t: Track) => {
                ListItem() {
                  Row() {
                    Column() {
                      Text(t.title)
                        .fontSize(15)
                        .fontWeight(this.currentId === t.id ? FontWeight.Bold : FontWeight.Normal)
                        .fontColor(this.currentId === t.id ? '#5b6ef5' : '#1a1a1a')
                      Text(t.artist && t.artist.length > 0 ? t.artist : '未知歌手')
                        .fontSize(12)
                        .fontColor('#888888')
                        .margin({ top: 2 })
                    }
                    .layoutWeight(1)
                    .alignItems(HorizontalAlign.Start)

                    if (this.currentId === t.id) {
                      Text('▶').fontSize(14).fontColor('#5b6ef5').margin({ right: 8 })
                    }
                    if (t.sourceType === SourceType.Cloud && t.isCached) {
                      Text('已缓存').fontSize(11).fontColor('#34c759').margin({ right: 8 })
                    }
                    Button({ type: ButtonType.Circle }) {
                      Text('✕').fontSize(14).fontColor('#888888')
                    }
                    .width(28).height(28).backgroundColor('#f2f2f2')
                    .onClick(() => { this.vm.removeSelected([t.id]); })
                  }
                  .width('100%')
                  .padding({ left: 16, right: 12, top: 10, bottom: 10 })
                }
                .onClick(() => { this.vm.playTrack(t); })
              }, (t: Track) => t.id)
            }
            .layoutWeight(1)
            .width('100%')
            .divider({ strokeWidth: 1, color: '#f2f2f6' })
          }
        } else {
          Text('暂无历史记录')
            .fontSize(14)
            .fontColor('#888888')
            .width('100%')
            .textAlign(TextAlign.Center)
            .padding({ top: 40, bottom: 40 })
        }
      }
      .width('100%')
      .height('62%')
      .backgroundColor('#ffffff')
      .borderRadius({ topLeft: 20, topRight: 20 })
      .align(Alignment.Bottom)
    }
    .width('100%')
    .height('100%')
  }

  build() {
    Stack() {
      // 主播放页
      Column() {
        // 顶部栏：标题 + 设置（更多）入口
        Row() {
          Text('正在播放').fontSize(18).fontWeight(FontWeight.Bold).fontColor('#1a1a1a')
          Blank().layoutWeight(1)
          Button({ type: ButtonType.Circle }) {
            Text('⚙').fontSize(20).fontColor('#1a1a1a')
          }
          .width(40).height(40).backgroundColor('#f0f0f5')
          .onClick(() => { router.pushUrl({ url: 'pages/MorePage' }); })
        }
        .width('100%')
        .padding({ left: 16, right: 12, top: 12, bottom: 8 })

        // 大封面（占位：渐变 + 音符；日后可接入真实专辑图）
        Column() {
          Stack() {
            Stack()
              .width(260).height(260)
              .borderRadius(28)
              .linearGradient({ angle: 135, colors: [[0x5b6ef5, 0.0], [0x9b5bf5, 1.0]] })
              .shadow({ radius: 24, color: '#00000022', offsetX: 0, offsetY: 12 })
            Text('♫').fontSize(110).fontColor('#ffffff').opacity(0.92)
          }
        }
        .layoutWeight(1)
        .width('100%')
        .justifyContent(FlexAlign.Center)

        // 歌名 / 歌手
        Text(this.currentTitle)
          .fontSize(26).fontWeight(FontWeight.Bold).fontColor('#1a1a1a')
          .textAlign(TextAlign.Center).width('100%').margin({ top: 12, bottom: 4 })
        Text(this.currentArtist && this.currentArtist.length > 0 ? this.currentArtist : '未知歌手')
          .fontSize(16).fontColor('#888888')
          .textAlign(TextAlign.Center).width('100%')

        // 当前歌词行（轻提示）
        if (this.activeLyricIndex >= 0 && this.lyricsLines.length > 0) {
          Text(this.lyricsLines[this.activeLyricIndex].text)
            .fontSize(14).fontColor('#bbbbbb')
            .textAlign(TextAlign.Center).width('100%').margin({ top: 10 })
        }

        // 进度条 + 时间
        Row() {
          Text(this.formatTime(this.positionMs)).fontSize(12).fontColor('#888888').width(40)
          Slider({
            value: this.positionMs,
            min: 0,
            max: this.durationMs > 0 ? this.durationMs : 1,
            step: 500
          })
            .layoutWeight(1)
            .blockColor('#5b6ef5')
            .selectedColor('#5b6ef5')
            .trackColor('#e6e6ef')
            .onChange((v: number) => { this.vm.seek(v); })
          Text(this.formatTime(this.durationMs)).fontSize(12).fontColor('#888888').width(40)
        }
        .width('100%')
        .padding({ left: 20, right: 20, top: 14, bottom: 6 })

        // 播放控制：模式 / 上一首 / 播放暂停 / 下一首 / 播放列表
        Row() {
          // 模式切换（小图标，上一首左侧）
          Button({ type: ButtonType.Circle }) {
            Text(this.modeIcon()).fontSize(18)
          }
          .width(44).height(44).backgroundColor('#eef0f7')
          .margin({ right: 18 })
          .onClick(() => { this.cycleMode(); })

          Button({ type: ButtonType.Circle }) {
            Text('⏮').fontSize(24).fontColor('#1a1a1a')
          }
          .width(58).height(58).backgroundColor('#eef0f7')
          .onClick(() => { this.vm.prev(); })

          Button({ type: ButtonType.Circle }) {
            Text(this.isPlaying ? '⏸' : '▶').fontSize(32).fontColor('#ffffff')
          }
          .width(76).height(76).backgroundColor('#5b6ef5')
          .margin({ left: 28, right: 28 })
          .onClick(() => { this.vm.togglePlay(); })

          Button({ type: ButtonType.Circle }) {
            Text('⏭').fontSize(24).fontColor('#1a1a1a')
          }
          .width(58).height(58).backgroundColor('#eef0f7')
          .onClick(() => { this.vm.next(); })

          // 播放列表抽屉按钮（下一首右侧）
          Button({ type: ButtonType.Circle }) {
            Text('☰').fontSize(20).fontColor('#1a1a1a')
          }
          .width(44).height(44).backgroundColor('#eef0f7')
          .margin({ left: 18 })
          .onClick(() => { this.showPlaylist = true; })
        }
        .justifyContent(FlexAlign.Center)
        .width('100%')
        .margin({ top: 6, bottom: 28 })
      }
      .width('100%')
      .height('100%')
      .backgroundColor('#ffffff')

      // 播放列表抽屉覆盖层
      if (this.showPlaylist) {
        this.PlaylistDrawer()
      }
    }
    .width('100%')
    .height('100%')
  }
}
