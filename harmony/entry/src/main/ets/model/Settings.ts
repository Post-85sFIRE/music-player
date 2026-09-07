// 设置持久化：基于 @ohos.data.preferences，存音量 / 播放模式 / 网络歌词开关 / ReplayGain 模式 / 云盘配置 / 播放列表。
// 云盘密码走设备级加密（SecureStore / Asset），不明文落盘。
import dataPreferences from '@ohos.data.preferences';
import { common } from '@kit.AbilityKit';
import { SecureStore } from './SecureStore';
import { SourceType } from './Track';

export class CloudConfig {
  id: string = '';
  name: string = '';
  baseUrl: string = '';
  userName: string = '';
  password: string = ''; // 运行时使用；持久化时存 Asset，preferences 仅存非敏感字段
}

// 播放列表持久化 DTO（顺序 + 曲目元信息）。
export interface PlaylistDto {
  id: string;
  uri: string;
  title: string;
  artist: string;
  album: string;
  sourceType: SourceType;
  sourceId: string;
  order: number;
}

export class AppSettings {
  volume: number = 0.8;
  playMode: number = 0; // 0 顺序 / 1 单曲循环 / 2 随机
  onlineLyricsEnabled: boolean = true; // 默认开启网络歌词（对齐 Windows 端）
  replayGainMode: number = 0; // 0 关 / 1 专辑(-3dB) / 2 单曲(-6dB)；鸿蒙无 TagLib，仅全局音量系数近似
  cloudSources: CloudConfig[] = [];
  playlist: PlaylistDto[] = [];
}

const PWD_PREFIX: string = 'musicplayer_cloud_pwd_';

export class SettingsStore {
  private static readonly NAME: string = 'musicplayer_prefs';
  private prefs: dataPreferences.Preferences | undefined = undefined;

  async init(context: common.UIAbilityContext): Promise<void> {
    this.prefs = await dataPreferences.getPreferences(context, SettingsStore.NAME);
  }

  async load(): Promise<AppSettings> {
    const s = new AppSettings();
    if (!this.prefs) {
      return s;
    }
    s.volume = this.prefs.getSync('volume', 0.8) as number;
    s.playMode = this.prefs.getSync('playMode', 0) as number;
    s.onlineLyricsEnabled = this.prefs.getSync('onlineLyricsEnabled', true) as boolean;
    s.replayGainMode = this.prefs.getSync('replayGainMode', 0) as number;

    const cloudRaw = this.prefs.getSync('cloudSources', '[]') as string;
    try {
      const cfgs = JSON.parse(cloudRaw) as CloudConfig[];
      for (const c of cfgs) {
        c.password = (await SecureStore.getSecret(PWD_PREFIX + c.id)) ?? '';
      }
      s.cloudSources = cfgs;
    } catch (e) {
      s.cloudSources = [];
    }

    const plRaw = this.prefs.getSync('playlist', '[]') as string;
    try {
      s.playlist = JSON.parse(plRaw) as PlaylistDto[];
    } catch (e) {
      s.playlist = [];
    }
    return s;
  }

  async save(s: AppSettings): Promise<void> {
    if (!this.prefs) {
      return;
    }
    this.prefs.putSync('volume', s.volume);
    this.prefs.putSync('playMode', s.playMode);
    this.prefs.putSync('onlineLyricsEnabled', s.onlineLyricsEnabled);
    this.prefs.putSync('replayGainMode', s.replayGainMode);

    // 云盘：非敏感字段存 preferences，密码存 Asset（按 id 派生 alias）
    const forPrefs = s.cloudSources.map((c) => ({
      id: c.id,
      name: c.name,
      baseUrl: c.baseUrl,
      userName: c.userName
    }));
    this.prefs.putSync('cloudSources', JSON.stringify(forPrefs));
    for (const c of s.cloudSources) {
      if (c.password) {
        await SecureStore.saveSecret(PWD_PREFIX + c.id, c.password);
      } else {
        await SecureStore.deleteSecret(PWD_PREFIX + c.id);
      }
    }

    this.prefs.putSync('playlist', JSON.stringify(s.playlist));
    await this.prefs.flush();
  }

  // 仅保存播放列表（切歌/排序/增删后轻量持久化，不触发整份刷新）
  async savePlaylist(playlist: PlaylistDto[]): Promise<void> {
    if (!this.prefs) {
      return;
    }
    this.prefs.putSync('playlist', JSON.stringify(playlist));
    await this.prefs.flush();
  }
}
