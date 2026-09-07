// 云盘页：管理 WebDAV 源、浏览目录、将云文件加入播放列表。
// 风格与设置页一致：浅灰背景 + 白色圆角卡片。
import { MainViewModel } from '../viewmodel/MainViewModel';
import { CloudConfig, SettingsStore } from '../model/Settings';
import { CloudEntry } from '../model/WebDavClient';
import { router } from '@kit.ArkUI';
import { common } from '@kit.AbilityKit';

@Entry
@Component
struct CloudPage {
  vm: MainViewModel = new MainViewModel(getContext(this) as common.UIAbilityContext, new SettingsStore());

  @State cloudSources: CloudConfig[] = [];
  @State cloudEntries: CloudEntry[] = [];
  @State status: string = '就绪';

  // 新增云盘表单
  @State newName: string = '';
  @State newUrl: string = '';
  @State newUser: string = '';
  @State newPwd: string = '';

  aboutToAppear(): void {
    const stored = AppStorage.get('vm') as MainViewModel;
    if (stored) {
      this.vm = stored;
    }
    this.vm.onCloudChanged = () => {
      this.cloudSources = this.vm.cloudSources.slice();
      this.cloudEntries = this.vm.cloudEntries.slice();
      this.status = this.vm.status;
    };
    this.mirrorAll();
  }

  onPageShow(): void {
    this.mirrorAll();
  }

  private mirrorAll(): void {
    this.cloudSources = this.vm.cloudSources.slice();
    this.cloudEntries = this.vm.cloudEntries.slice();
    this.status = this.vm.status;
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

        Text('云盘').fontSize(18).fontWeight(FontWeight.Bold).fontColor('#1a1a1a')
        Blank().layoutWeight(1)
      }
      .width('100%')
      .padding({ left: 8, right: 16, top: 12, bottom: 8 })

      Scroll() {
        Column() {
          // 添加云盘卡片
          Column() {
            Row() {
              Stack() {
                Text('☁').fontSize(18).fontColor('#ffffff')
              }
              .width(32).height(32).borderRadius(8).backgroundColor('#3ba3f5')
              .margin({ right: 12 })
              Text('添加云盘').fontSize(16).fontWeight(FontWeight.Bold).fontColor('#1a1a1a').layoutWeight(1)
            }
            .width('100%')
            .padding({ left: 16, right: 12, top: 14, bottom: 10 })

            Divider().color('#f2f2f6').strokeWidth(1)

            Column() {
              TextInput({ placeholder: '名称', text: this.newName })
                .width('100%').height(40).margin({ top: 8 })
                .onChange((v: string) => { this.newName = v; })
              TextInput({ placeholder: 'WebDAV 地址', text: this.newUrl })
                .width('100%').height(40).margin({ top: 8 })
                .onChange((v: string) => { this.newUrl = v; })
              TextInput({ placeholder: '用户名（坚果云为应用密码账号）', text: this.newUser })
                .width('100%').height(40).margin({ top: 8 })
                .onChange((v: string) => { this.newUser = v; })
              TextInput({ placeholder: '密码 / 应用密码', text: this.newPwd })
                .width('100%').height(40).margin({ top: 8 })
                .type(InputType.Password)
                .onChange((v: string) => { this.newPwd = v; })
              Button('连接并保存')
                .width('100%').margin({ top: 12, bottom: 4 })
                .onClick(async () => {
                  if (this.newUrl.trim().length > 0) {
                    const ok: boolean = await this.vm.connectCloud(this.newName.trim(), this.newUrl.trim(), this.newUser.trim(), this.newPwd);
                    if (ok) {
                      // 连接成功才清空表单；失败保留数据方便修改
                      this.newName = '';
                      this.newUrl = '';
                      this.newUser = '';
                      this.newPwd = '';
                    }
                    this.cloudSources = this.vm.cloudSources.slice();
                  }
                })
            }
            .width('100%')
            .padding({ left: 16, right: 16, bottom: 12 })
          }
          .width('100%')
          .backgroundColor('#ffffff')
          .borderRadius(12)
          .margin({ left: 16, right: 16, top: 8 })

          // 已配置云盘卡片
          Column() {
            Row() {
              Stack() {
                Text('☁').fontSize(18).fontColor('#ffffff')
              }
              .width(32).height(32).borderRadius(8).backgroundColor('#34c759')
              .margin({ right: 12 })
              Text('已配置云盘').fontSize(16).fontWeight(FontWeight.Bold).fontColor('#1a1a1a').layoutWeight(1)
            }
            .width('100%')
            .padding({ left: 16, right: 12, top: 14, bottom: 10 })

            if (this.cloudSources.length === 0) {
              Divider().color('#f2f2f6').strokeWidth(1)
              Text('尚未配置云盘').fontSize(14).fontColor('#8e8e93').width('100%').padding(16)
            }

            ForEach(this.cloudSources, (s: CloudConfig) => {
              Divider().color('#f2f2f6').strokeWidth(1).margin({ left: 16 })
              Column() {
                Row() {
                  Text(s.name).fontSize(15).fontWeight(FontWeight.Bold).layoutWeight(1)
                  Text(this.vm.isConnected(s.id) ? '已连接' : '未连接')
                    .fontSize(12)
                    .fontColor(this.vm.isConnected(s.id) ? '#34c759' : '#ff3b30')
                }.width('100%')
                Text(s.userName).fontSize(12).fontColor('#8e8e93').margin({ top: 2, bottom: 6 })
                Row() {
                  Button('重连', { type: ButtonType.Capsule })
                    .fontSize(12).height(28).backgroundColor('#f2f2f6').fontColor('#1a1a1a')
                    .onClick(() => { this.vm.reconnectCloud(s.id); })
                  Button('移除', { type: ButtonType.Capsule })
                    .fontSize(12).height(28).backgroundColor('#f2f2f6').fontColor('#ff3b30')
                    .margin({ left: 8 })
                    .onClick(() => { this.vm.removeCloud(s.id); this.cloudSources = this.vm.cloudSources.slice(); })
                }
              }
              .width('100%')
              .padding({ left: 16, right: 16, top: 10, bottom: 10 })
            }, (s: CloudConfig) => s.id)
          }
          .width('100%')
          .backgroundColor('#ffffff')
          .borderRadius(12)
          .margin({ left: 16, right: 16, top: 12 })

          // 云盘浏览卡片
          Column() {
            Row() {
              Stack() {
                Text('📂').fontSize(18).fontColor('#ffffff')
              }
              .width(32).height(32).borderRadius(8).backgroundColor('#ff9500')
              .margin({ right: 12 })
              Text('浏览云盘').fontSize(16).fontWeight(FontWeight.Bold).fontColor('#1a1a1a').layoutWeight(1)
            }
            .width('100%')
            .padding({ left: 16, right: 12, top: 14, bottom: 10 })

            Divider().color('#f2f2f6').strokeWidth(1)

            Row() {
              ForEach(this.cloudSources, (s: CloudConfig) => {
                Button(s.name, { type: ButtonType.Capsule })
                  .fontSize(12).height(28).backgroundColor('#f2f2f6').fontColor('#1a1a1a')
                  .margin(4)
                  .onClick(() => { this.vm.browseCloud(s.id, ''); })
              }, (s: CloudConfig) => s.id)
              if (this.cloudSources.length === 0) {
                Text('先添加云盘').fontSize(12).fontColor('#8e8e93')
              }
            }
            .width('100%')
            .padding({ left: 12, right: 12, top: 8, bottom: 8 })

            Row() {
              Button('返回上级', { type: ButtonType.Capsule })
                .fontSize(12).height(28).backgroundColor('#f2f2f6').fontColor('#1a1a1a')
                .onClick(() => { this.vm.upCloud(); })
              Blank().layoutWeight(1)
              Text(this.status).fontSize(12).fontColor('#8e8e93')
            }
            .width('100%')
            .padding({ left: 16, right: 16, top: 4, bottom: 8 })

            Divider().color('#f2f2f6').strokeWidth(1)

            ForEach(this.cloudEntries, (e: CloudEntry) => {
              Row() {
                Text(e.isFolder ? '📁' : '🎵').fontSize(16).margin({ right: 10 })
                Text(e.name).fontSize(15).layoutWeight(1)
                if (e.isCached) {
                  Text('已缓存').fontColor('#34c759').fontSize(12).margin({ right: 8 })
                }
                if (e.isFolder) {
                  Button('打开', { type: ButtonType.Capsule })
                    .fontSize(12).height(28).backgroundColor('#f2f2f6').fontColor('#1a1a1a')
                    .onClick(() => { this.vm.browseCloud(this.vm.selectedSourceId, e.id); })
                } else {
                  Button('加入列表', { type: ButtonType.Capsule })
                    .fontSize(12).height(28).backgroundColor('#f2f2f6').fontColor('#1a1a1a')
                    .onClick(() => { this.vm.addCloudToPlaylist(this.vm.selectedSourceId, e); })
                }
              }
              .width('100%')
              .padding({ left: 16, right: 16, top: 10, bottom: 10 })
            }, (e: CloudEntry) => e.id)
          }
          .width('100%')
          .backgroundColor('#ffffff')
          .borderRadius(12)
          .margin({ left: 16, right: 16, top: 12, bottom: 24 })
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
