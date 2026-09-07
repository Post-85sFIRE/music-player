// 更多页：从播放页右上角进入，提供 设置 / 云盘 / 关于 三个入口。
// 风格参考系统设置：浅灰背景 + 白色圆角卡片 + 左侧图标 + 右侧箭头。
import { router } from '@kit.ArkUI';

@Entry
@Component
struct MorePage {
  private navigate(url: string): void {
    router.pushUrl({ url: url });
  }

  @Builder
  MenuItem(icon: string, iconBg: ResourceColor, title: string, url: string) {
    Row() {
      Stack() {
        Text(icon).fontSize(20).fontColor('#ffffff')
      }
      .width(36).height(36).borderRadius(8).backgroundColor(iconBg)
      .margin({ right: 12 })

      Text(title).fontSize(16).fontColor('#1a1a1a').layoutWeight(1)
      Text('›').fontSize(22).fontColor('#c7c7cc')
    }
    .width('100%')
    .padding({ left: 16, right: 12, top: 14, bottom: 14 })
    .backgroundColor('#ffffff')
    .onClick(() => { this.navigate(url); })
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

      // 三个入口卡片
      Column() {
        this.MenuItem('⚙', '#5b6ef5', '设置', 'pages/SettingsPage')
        Divider().color('#f2f2f6').strokeWidth(1).margin({ left: 64 })
        this.MenuItem('☁', '#3ba3f5', '云盘', 'pages/CloudPage')
        Divider().color('#f2f2f6').strokeWidth(1).margin({ left: 64 })
        this.MenuItem('ℹ', '#8e8e93', '关于', 'pages/AboutPage')
      }
      .width('100%')
      .backgroundColor('#ffffff')
      .borderRadius(12)
      .margin({ left: 16, right: 16, top: 8 })
    }
    .width('100%')
    .height('100%')
    .backgroundColor('#f2f2f6')
  }
}
