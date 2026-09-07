// 主视图模型：组合播放引擎、播放列表、本地扫描、云盘客户端、歌词服务与设置。
// 二期增量：启动自动重连云盘（密码走设备级加密）、云/主列表"已缓存"标记、歌词三层+搜索、ReplayGain、播放列表持久化。
// UI（Index.ets / SettingsPage.ets）通过回调镜像这些字段以保持响应式。
import { common } from '@kit.AbilityKit';
import { Track, SourceType } from '../model/Track';
import { PlayerEngine, PlayMode } from '../model/PlayerEngine';
import { PlaylistManager } from '../model/PlaylistManager';
import { LibraryScanner } from '../model/LibraryScanner';
import { WebDavClient, CloudEntry } from '../model/WebDavClient';
import { SettingsStore, CloudConfig, AppSettings } from '../model/Settings';
import { LyricsService } from '../model/LyricsService';
import { LyricLine } from '../model/Lyrics';

export class MainViewModel {
  private engine: PlayerEngine = new PlayerEngine();
  private playlist: PlaylistManager = new PlaylistManager();
  private scanner: LibraryScanner = new LibraryScanner();
  private settingsStore: SettingsStore;
  private settings: AppSettings = new AppSettings();
  private cloudClients: Map<string, WebDavClient> = new Map();
  private connectedMap: Map<string, boolean> = new Map();
  private lyrics: LyricsService = new LyricsService();

  // 对外数据（页面通过回调镜像为 @State）
  library: Track[] = [];
  current: Track | null = null;
  isPlaying: boolean = false;
  positionMs: number = 0;
  durationMs: number = 0;
  volume: number = 0.8;
  playMode: PlayMode = PlayMode.Order;
  status: string = '就绪';
  cloudSources: CloudConfig[] = [];
  cloudEntries: CloudEntry[] = [];
  selectedSourceId: string = '';
  currentCloudFolder: string = '';
  onlineLyricsEnabled: boolean = true;
  replayGainMode: number = 0;
  lyricsLines: LyricLine[] = [];
  lyricsSource: string = '';

  // 回调（由页面赋值）
  onLibraryChanged?: () => void;
  onCurrentChanged?: () => void;
  onPlayStateChanged?: () => void;
  onProgress?: () => void;
  onCloudChanged?: () => void;
  onLyricsChanged?: () => void;
  onSettingsChanged?: () => void;

  constructor(private context: common.UIAbilityContext, settingsStore: SettingsStore) {
    this.settingsStore = settingsStore;
  }

  async init(): Promise<void> {
    this.settings = await this.settingsStore.load();
    this.volume = this.settings.volume;
    this.playMode = this.settings.playMode as PlayMode;
    this.onlineLyricsEnabled = this.settings.onlineLyricsEnabled;
    this.replayGainMode = this.settings.replayGainMode;

    // 启动自动重连已配置的云盘（密码已从设备级加密读取）
    await this.reconnectCloud();

    // 恢复播放列表并标记云曲缓存状态
    this.playlist.restore(this.settings.playlist);
    for (const t of this.playlist.all) {
      if (t.sourceType === SourceType.Cloud && t.sourceId) {
        const client = this.cloudClients.get(t.sourceId);
        if (client) {
          t.isCached = await client.isCached(t.uri);
        }
      }
    }
    this.library = this.playlist.all.slice();

    this.engine.onStateChange = (s: string) => {
      this.isPlaying = s === 'playing';
      this.onPlayStateChanged?.();
    };
    this.engine.onDuration = (d: number) => {
      this.durationMs = d;
      this.onProgress?.();
    };
    this.engine.onTime = (t: number) => {
      this.positionMs = t;
      this.onProgress?.();
    };
    this.engine.onEnded = () => {
      this.onTrackEnded();
    };
    this.engine.onError = (msg: string) => {
      this.status = '播放错误：' + msg;
    };

    await this.engine.create();
    this.engine.setVolume(this.volume * this.gainCoef());
    this.status = this.status.startsWith('自动') || this.status.startsWith('已连接') || this.status.startsWith('重连')
      ? this.status
      : (this.cloudSources.length > 0 ? this.status : '就绪');
  }

  private gainCoef(): number {
    if (this.replayGainMode === 1) {
      return 0.708; // 专辑 -3dB
    }
    if (this.replayGainMode === 2) {
      return 0.5; // 单曲 -6dB
    }
    return 1.0;
  }

  isConnected(sourceId: string): boolean {
    return this.connectedMap.get(sourceId) === true;
  }

  // 重连全部或指定云盘（启动自动重连 / 设置页"重连"按钮共用）
  async reconnectCloud(sourceId?: string): Promise<void> {
    const targets = sourceId ? this.cloudSources.filter((c) => c.id === sourceId) : this.cloudSources;
    let okCount = 0;
    const failed: string[] = [];
    for (const cfg of targets) {
      let client = this.cloudClients.get(cfg.id);
      if (!client) {
        client = new WebDavClient(cfg.baseUrl, cfg.userName, cfg.password, this.context);
      }
      const login = await client.login();
      this.cloudClients.set(cfg.id, client);
      this.connectedMap.set(cfg.id, login.ok);
      if (login.ok) {
        okCount++;
      } else {
        failed.push(cfg.name);
      }
    }
    this.cloudSources = this.settings.cloudSources;
    if (this.cloudSources.length === 0) {
      this.status = '就绪';
    } else if (failed.length === 0) {
      this.status = `已自动连接 ${okCount} 个云盘`;
    } else if (okCount === 0) {
      this.status = '自动重连失败：' + failed.join('、');
    } else {
      this.status = `已自动连接 ${okCount} 个，失败：${failed.join('、')}`;
    }
    this.onCloudChanged?.();
  }

  async addLocalMusic(): Promise<void> {
    const tracks: Track[] = await this.scanner.pickAudio();
    if (tracks.length === 0) {
      return;
    }
    this.playlist.addAll(tracks);
    this.library = this.playlist.all.slice();
    this.persistPlaylist();
    this.onLibraryChanged?.();
    this.status = `已添加 ${tracks.length} 首本地音乐`;
  }

  async playTrack(t: Track): Promise<void> {
    this.current = t;
    this.onCurrentChanged?.();
    await this.loadAndPlay(t);
  }

  private async loadAndPlay(t: Track): Promise<void> {
    try {
      if (t.sourceType === SourceType.Cloud) {
        const client: WebDavClient | undefined = this.cloudClients.get(t.sourceId);
        if (!client) {
          this.status = '云源未连接';
          return;
        }
        this.status = '正在缓存云文件…';
        const local: string = await client.cacheAndGetLocal(t.uri);
        t.isCached = true;
        await this.engine.load(local);
      } else {
        await this.engine.load(t.uri);
      }
      // 加载歌词（后台）
      this.loadLyrics(t);
    } catch (e) {
      this.status = '加载失败：' + JSON.stringify(e);
    }
  }

  togglePlay(): void {
    if (!this.current) {
      return;
    }
    if (this.isPlaying) {
      this.engine.pause();
    } else {
      this.engine.play();
    }
  }

  next(): void {
    const t: Track | null = this.playlist.next(this.playMode);
    if (t) {
      this.current = t;
      this.onCurrentChanged?.();
      this.loadAndPlay(t);
    }
  }

  prev(): void {
    const t: Track | null = this.playlist.prev(this.playMode);
    if (t) {
      this.current = t;
      this.onCurrentChanged?.();
      this.loadAndPlay(t);
    }
  }

  seek(ms: number): void {
    this.engine.seek(ms);
  }

  setVolume(v: number): void {
    this.volume = v;
    this.engine.setVolume(v * this.gainCoef());
    this.settings.volume = v;
    this.settingsStore.save(this.settings);
  }

  setMode(m: PlayMode): void {
    this.playMode = m;
    this.settings.playMode = m;
    this.settingsStore.save(this.settings);
    this.engine.setLoop(m === PlayMode.LoopOne);
  }

  setOnlineLyrics(b: boolean): void {
    this.onlineLyricsEnabled = b;
    this.settings.onlineLyricsEnabled = b;
    this.settingsStore.save(this.settings);
    this.onSettingsChanged?.();
  }

  setReplayGain(mode: number): void {
    this.replayGainMode = mode;
    this.settings.replayGainMode = mode;
    this.engine.setVolume(this.volume * this.gainCoef());
    this.settingsStore.save(this.settings);
    this.onSettingsChanged?.();
  }

  private onTrackEnded(): void {
    if (this.playMode === PlayMode.LoopOne) {
      if (this.current) {
        this.loadAndPlay(this.current);
      }
      return;
    }
    this.next();
  }

  // ---- 歌词 ----
  private async loadLyrics(t: Track): Promise<void> {
    this.lyricsLines = [];
    this.lyricsSource = '歌词加载中…';
    this.onLyricsChanged?.();
    const client: WebDavClient | undefined = t.sourceType === SourceType.Cloud ? this.cloudClients.get(t.sourceId) : undefined;
    const result = await this.lyrics.loadAsync(t, this.settings.onlineLyricsEnabled, client);
    this.lyricsLines = result.lines;
    this.lyricsSource = result.lines.length > 0
      ? (result.source ? `歌词来源：${result.source}` : '纯文本歌词')
      : '暂无歌词（可在搜索框手动找）';
    this.onLyricsChanged?.();
  }

  async searchLyrics(query: string): Promise<void> {
    this.lyricsLines = [];
    this.lyricsSource = '搜索中…';
    this.onLyricsChanged?.();
    const result = await this.lyrics.searchAsync(query, this.settings.onlineLyricsEnabled);
    this.lyricsLines = result.lines;
    this.lyricsSource = result.lines.length > 0 ? `搜索结果：${result.source}` : '未找到歌词';
    this.onLyricsChanged?.();
  }

  // ---- 播放列表增强 ----
  reorder(from: number, to: number): void {
    this.playlist.reorder(from, to);
    this.library = this.playlist.all.slice();
    this.persistPlaylist();
    this.onLibraryChanged?.();
  }

  removeSelected(ids: string[]): void {
    this.playlist.removeSelected(ids);
    this.library = this.playlist.all.slice();
    this.persistPlaylist();
    this.onLibraryChanged?.();
  }

  clearAll(): void {
    this.playlist.clear();
    this.library = [];
    this.persistPlaylist();
    this.onLibraryChanged?.();
  }

  private persistPlaylist(): void {
    this.settings.playlist = this.playlist.toDtos();
    this.settingsStore.savePlaylist(this.settings.playlist);
  }

  // ---- 云盘 ----
  async connectCloud(name: string, url: string, user: string, pwd: string): Promise<boolean> {
    this.status = '正在连接云盘…';
    this.onCloudChanged?.();
    const client: WebDavClient = new WebDavClient(url, user, pwd, this.context);
    const login = await client.login();
    if (!login.ok) {
      this.status = '云盘连接失败：' + (login.error || '请检查地址/账号/密码');
      this.onCloudChanged?.();
      return false;
    }
    const cfg: CloudConfig = {
      id: 'c_' + Date.now().toString(),
      name: name || url,
      baseUrl: url,
      userName: user,
      password: pwd
    };
    this.cloudClients.set(cfg.id, client);
    this.connectedMap.set(cfg.id, true);
    this.cloudSources.push(cfg);
    this.selectedSourceId = cfg.id;
    this.settings.cloudSources = this.cloudSources;
    await this.settingsStore.save(this.settings);
    this.onCloudChanged?.();
    this.status = '已连接云盘：' + cfg.name;
    await this.browseCloud(cfg.id, '');
    return true;
  }

  async removeCloud(sourceId: string): Promise<void> {
    this.cloudClients.delete(sourceId);
    this.connectedMap.delete(sourceId);
    this.cloudSources = this.cloudSources.filter((c) => c.id !== sourceId);
    this.settings.cloudSources = this.cloudSources;
    await this.settingsStore.save(this.settings);
    this.onCloudChanged?.();
  }

  async browseCloud(sourceId: string, folder: string): Promise<void> {
    const client: WebDavClient | undefined = this.cloudClients.get(sourceId);
    if (!client) {
      return;
    }
    this.selectedSourceId = sourceId;
    const entries = await client.list(folder);
    for (const e of entries) {
      e.isCached = await client.isCached(e.id);
    }
    this.cloudEntries = entries;
    this.currentCloudFolder = folder;
    this.onCloudChanged?.();
  }

  async upCloud(): Promise<void> {
    if (!this.selectedSourceId) {
      return;
    }
    const folder: string = this.currentCloudFolder;
    const trimmed: string = folder.replace(/\/$/, '');
    const idx: number = trimmed.lastIndexOf('/');
    const parent: string = idx <= 0 ? '' : trimmed.substring(0, idx);
    await this.browseCloud(this.selectedSourceId, parent);
  }

  async addCloudToPlaylist(sourceId: string, entry: CloudEntry): Promise<void> {
    if (entry.isFolder) {
      return;
    }
    const t: Track = new Track();
    t.id = 'cloud:' + sourceId + '|' + entry.id;
    t.uri = entry.id;
    t.title = entry.name;
    t.sourceType = SourceType.Cloud;
    t.sourceId = sourceId;
    const client = this.cloudClients.get(sourceId);
    if (client) {
      t.isCached = await client.isCached(entry.id);
    }
    this.playlist.addAll([t]);
    this.library = this.playlist.all.slice();
    this.persistPlaylist();
    this.onLibraryChanged?.();
    this.status = '已加入列表：' + entry.name;
  }
}
