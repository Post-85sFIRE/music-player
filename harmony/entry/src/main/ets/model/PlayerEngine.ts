// AVPlayer 播放引擎封装：处理状态机（idle→initialized→prepared→playing→paused→completed），
// 对外暴露简单的播放控制与进度/时长回调。直接对标 Windows 端的 BassAudioEngine。
import { media } from '@kit.MediaKit';

export enum PlayMode {
  Order = 0,
  LoopOne = 1,
  Shuffle = 2
}

export class PlayerEngine {
  private avPlayer: media.AVPlayer | null = null;
  private state: string = 'idle';
  private url: string = '';
  private pendingVolume: number = 0.8;
  private pendingLoop: boolean = false;

  // UI 回调
  onStateChange?: (state: string) => void;
  onDuration?: (ms: number) => void;
  onTime?: (ms: number) => void;
  onEnded?: () => void;
  onError?: (msg: string) => void;

  get currentState(): string {
    return this.state;
  }

  async create(): Promise<void> {
    if (this.avPlayer) {
      return;
    }
    this.avPlayer = await media.createAVPlayer();
    this.bind(this.avPlayer);
  }

  private bind(player: media.AVPlayer): void {
    player.on('stateChange', (state: string, reason: Object) => {
      this.state = state;
      this.onStateChange?.(state);
      if (state === 'initialized') {
        player.prepare();
      } else if (state === 'prepared') {
        player.setVolume(this.pendingVolume);
        player.loop = this.pendingLoop;
        player.play();
      } else if (state === 'completed') {
        this.onEnded?.();
      }
    });
    player.on('durationUpdate', (duration: number) => {
      this.onDuration?.(duration);
    });
    player.on('timeUpdate', (time: number) => {
      this.onTime?.(time);
    });
    player.on('error', (err: BusinessError) => {
      this.onError?.(`code=${err.code} msg=${err.message}`);
      player.reset();
    });
  }

  // 加载并自动播放（url 为 file:// 路径或 fd:// 描述符）
  async load(url: string): Promise<void> {
    if (!this.avPlayer) {
      await this.create();
    }
    if (this.url === url && (this.state === 'playing' || this.state === 'paused')) {
      return;
    }
    this.url = url;
    if (this.state !== 'idle') {
      await this.avPlayer!.reset();
    }
    this.avPlayer!.url = url;
  }

  play(): void {
    if (this.avPlayer && (this.state === 'paused' || this.state === 'prepared')) {
      this.avPlayer.play();
    }
  }

  pause(): void {
    if (this.avPlayer && this.state === 'playing') {
      this.avPlayer.pause();
    }
  }

  seek(ms: number): void {
    if (this.avPlayer && (this.state === 'playing' || this.state === 'paused')) {
      this.avPlayer.seek(ms);
    }
  }

  setVolume(v: number): void {
    this.pendingVolume = v;
    if (this.avPlayer && (this.state === 'prepared' || this.state === 'playing' || this.state === 'paused')) {
      this.avPlayer.setVolume(v);
    }
  }

  setLoop(loop: boolean): void {
    this.pendingLoop = loop;
    if (this.avPlayer && (this.state === 'prepared' || this.state === 'playing' || this.state === 'paused')) {
      this.avPlayer.loop = loop;
    }
  }

  async release(): Promise<void> {
    if (this.avPlayer) {
      await this.avPlayer.release();
      this.avPlayer = null;
    }
  }
}
