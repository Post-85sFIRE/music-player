// 应用入口：初始化设置存储与主视图模型，并放入 AppStorage 供页面取用。
import { AbilityConstant, UIAbility, Want } from '@kit.AbilityKit';
import { hilog } from '@kit.PerformanceAnalysisKit';
import { window } from '@kit.ArkUI';
import { SettingsStore } from '../model/Settings';
import { MainViewModel } from '../viewmodel/MainViewModel';

export default class EntryAbility extends UIAbility {
  private settingsStore: SettingsStore = new SettingsStore();

  async onCreate(want: Want, launchParam: AbilityConstant.LaunchParam): Promise<void> {
    hilog.info(0x0000, 'MusicPlayer', '%{public}s', 'Ability onCreate');
    await this.settingsStore.init(this.context);
    const vm: MainViewModel = new MainViewModel(this.context, this.settingsStore);
    await vm.init();
    AppStorage.setOrCreate('vm', vm);
  }

  onWindowStageCreate(windowStage: window.WindowStage): void {
    windowStage.loadContent('pages/Index', (err: BusinessError) => {
      if (err.code) {
        hilog.error(0x0000, 'MusicPlayer', 'Failed to load page: %{public}s', JSON.stringify(err));
      }
    });
  }
}
