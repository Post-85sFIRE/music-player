// 设置页：播放相关设置（音量 / 播放模式 / 网络歌词 / ReplayGain）。
// 风格参考系统设置：浅灰背景 + 白色圆角分组卡片 + 左侧图标 + 右侧当前值/箭头。
import { MainViewModel } from '../viewmodel/MainViewModel';
import { PlayMode } from '../model/PlayerEngine';
import { SettingsStore } from '../model/Settings';
import { router } from '@kit.ArkUI';
import { common } from '@kit.AbilityKit';

@Entry
@Component
struct SettingsPage {
  vm: MainViewModel = new MainViewModel(getContext(this) as common.UIAbilityContext, new SettingsStore());

  @State volume: number = 0.8;
  @State mode: number = 0;
  @State onlineLyricsEnabled: boolean = true;
  @State replayGainMode: number = 0;

  aboutToAppear(): void {
    const stored = AppStorage.get('vm') as MainViewModel;
    if (stored) {
      this.vm = stored;
    }
    this.vm.onSettingsChanged = () => {
      this.volume = this.vm.volume;
      this.mode = this.vm.playMode as number;
      this.onlineLyricsEnabled = this.vm.onlineLyricsEnabled;
      this.replayGainMode = this.vm.replayGainMode;
    };
    this.mirrorAll();
  }

  onPageShow(): void {
    this.mirrorAll();
  }

  private mirrorAll(): void {
    this.volume = this.vm.volume;
    this.mode = this.vm.playMode as number;
    this.onlineLyricsEnabled = this.vm.onlineLyricsEnabled;
    this.replayGainMode = this.vm.replayGainMode;
  }

  private modeText(): string {
    if (this.mode === PlayMode.LoopOne) {
      return '单曲循环';
    }
    if (this.mode === PlayMode.Shuffle) {
      return '随机';
    }
    return '顺序';
  }

  private replayGainText(): string {
    if (this.replayGainMode === 1) {
      return '专辑 (-3dB)';
    }
    if (this.replayGainMode === 2) {
      return '单曲 (-6dB)';
    }
    return '关';
  }

  // 左侧彩色图标徽标（纯值参数，规避 @Builder 不能接收 builder 参数的限制）
  @Builder
  IconBadge(icon: string, iconBg: ResourceColor) {
    Stack() {
      Text(icon).fontSize(18).fontColor('#ffffff')
    }
    .width(32).height(32).borderRadius(8).backgroundColor(iconBg)
    .margin({ right: 12 })
  }

  build() {
    Column() {
      // 顶部栏
      Row() {
        Button({ type: ButtonType.Circle }) {
          Text('‹').fontSize(24).fontColor('#1a1a1a')
        }
        .width(36).height(36).backgroundColor('#ffffff')
        .onClick(() => { router.back(); })

        Text('设置').fontSize(18).fontWeight(FontWeight.Bold).fontColor('#1a1a1a')
        Blank().layoutWeight(1)
      }
      .width('100%')
      .padding({ left: 8, right: 16, top: 12, bottom: 8 })

      Scroll() {
        Column() {
          // 播放设置卡片
          Column() {
            // 音量
            Row() {
              this.IconBadge('🔊', '#5b6ef5')
              Text('音量').fontSize(16).fontColor('#1a1a1a').layoutWeight(1)
              Slider({ value: this.volume, min: 0, max: 1, step: 0.05 })
                .width(120)
                .blockColor('#5b6ef5')
                .selectedColor('#5b6ef5')
                .trackColor('#e6e6ef')
                .onChange((v: number) => { this.volume = v; this.vm.setVolume(v); })
            }
            .width('100%')
            .padding({ left: 16, right: 12, top: 14, bottom: 14 })
            .backgroundColor('#ffffff')

            Divider().color('#f2f2f6').strokeWidth(1).margin({ left: 60 })

            // 播放模式
            Row() {
              this.IconBadge('➡', '#34c759')
              Text('播放模式').fontSize(16).fontColor('#1a1a1a').layoutWeight(1)
              Text(this.modeText()).fontSize(14).fontColor('#8e8e93').margin({ right: 4 })
              Text('›').fontSize(22).fontColor('#c7c7cc')
            }
            .width('100%')
            .padding({ left: 16, right: 12, top: 14, bottom: 14 })
            .backgroundColor('#ffffff')
            .onClick(() => {
              const n: number = (this.mode + 1) % 3;
              this.vm.setMode(n as PlayMode);
              this.mode = n;
            })

            Divider().color('#f2f2f6').strokeWidth(1).margin({ left: 60 })

            // 网络歌词
            Row() {
              this.IconBadge('🌐', '#ff9500')
              Text('网络歌词').fontSize(16).fontColor('#1a1a1a').layoutWeight(1)
              Text(this.onlineLyricsEnabled ? '开' : '关').fontSize(14).fontColor('#8e8e93').margin({ right: 4 })
              Text('›').fontSize(22).fontColor('#c7c7cc')
            }
            .width('100%')
            .padding({ left: 16, right: 12, top: 14, bottom: 14 })
            .backgroundColor('#ffffff')
            .onClick(() => {
              const b: boolean = !this.onlineLyricsEnabled;
              this.vm.setOnlineLyrics(b);
              this.onlineLyricsEnabled = b;
            })

            Divider().color('#f2f2f6').strokeWidth(1).margin({ left: 60 })

            // ReplayGain
            Row() {
              this.IconBadge('🔊', '#af52de')
              Text('ReplayGain').fontSize(16).fontColor('#1a1a1a').layoutWeight(1)
              Text(this.replayGainText()).fontSize(14).fontColor('#8e8e93').margin({ right: 4 })
              Text('›').fontSize(22).fontColor('#c7c7cc')
            }
            .width('100%')
            .padding({ left: 16, right: 12, top: 14, bottom: 14 })
            .backgroundColor('#ffffff')
            .onClick(() => {
              const n: number = (this.replayGainMode + 1) % 3;
              this.vm.setReplayGain(n);
              this.replayGainMode = n;
            })
          }
          .width('100%')
          .backgroundColor('#ffffff')
          .borderRadius(12)
          .margin({ left: 16, right: 16, top: 8 })
        }
        .width('100%')
      }
      .layoutWeight(1)
      .width('100%')
    }
    .width('100%')
    .height('100%')
    .backgroundColor('#f2f2f6')
  }
}
