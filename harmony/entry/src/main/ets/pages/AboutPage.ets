// 关于页：应用信息。
// 风格与设置页一致：浅灰背景 + 白色圆角卡片。
import { router } from '@kit.ArkUI';

@Entry
@Component
struct AboutPage {
  build() {
    Column() {
      // 顶部栏
      Row() {
        Button({ type: ButtonType.Circle }) {
          Text('‹').fontSize(24).fontColor('#1a1a1a')
        }
        .width(36).height(36).backgroundColor('#ffffff')
        .onClick(() => { router.back(); })

        Text('关于').fontSize(18).fontWeight(FontWeight.Bold).fontColor('#1a1a1a')
        Blank().layoutWeight(1)
      }
      .width('100%')
      .padding({ left: 8, right: 16, top: 12, bottom: 8 })

      // 应用信息卡片
      Column() {
        Row() {
          Stack() {
            Text('♫').fontSize(28).fontColor('#ffffff')
          }
          .width(60).height(60).borderRadius(14).backgroundColor('#5b6ef5')
          .margin({ right: 16 })

          Column() {
            Text('MusicPlayer').fontSize(20).fontWeight(FontWeight.Bold).fontColor('#1a1a1a')
            Text('版本 1.0.0').fontSize(14).fontColor('#8e8e93').margin({ top: 4 })
          }
          .alignItems(HorizontalAlign.Start)
          .layoutWeight(1)
        }
        .width('100%')
        .padding(20)
      }
      .width('100%')
      .backgroundColor('#ffffff')
      .borderRadius(12)
      .margin({ left: 16, right: 16, top: 8 })

      // 说明卡片
      Column() {
        Text('一款跨平台音乐播放器。\nWindows 端基于 WPF + BASS，\n鸿蒙端基于 ArkTS + Stage 模型。\n支持本地音乐库、坚果云 WebDAV、歌词显示。')
          .fontSize(14)
          .fontColor('#555555')
          .lineHeight(22)
          .width('100%')
          .padding(16)
      }
      .width('100%')
      .backgroundColor('#ffffff')
      .borderRadius(12)
      .margin({ left: 16, right: 16, top: 12 })
    }
    .width('100%')
    .height('100%')
    .backgroundColor('#f2f2f6')
  }
}
