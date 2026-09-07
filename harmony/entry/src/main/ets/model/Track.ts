// 曲目模型：本地文件或云盘条目，统一由播放引擎消费。
import { PlaylistDto } from './Settings';

export enum SourceType {
  Local = 'Local',
  Cloud = 'Cloud'
}

export class Track {
  id: string = '';
  title: string = '';
  artist: string = '';
  album: string = '';
  // 本地为文件 URI；云盘为条目 ID（href）
  uri: string = '';
  sourceType: SourceType = SourceType.Local;
  // 云盘来源 ID（CloudSourceConfig.id）
  sourceId: string = '';
  durationMs: number = 0;
  size: number = 0;
  // 仅云曲目使用：是否已缓存到沙箱（列表标记用，不持久化）
  isCached: boolean = false;

  static fromLocal(uri: string, title: string): Track {
    const t = new Track();
    t.id = 'local:' + uri;
    t.uri = uri;
    t.title = title;
    t.sourceType = SourceType.Local;
    return t;
  }

  toDto(order: number): PlaylistDto {
    return {
      id: this.id,
      uri: this.uri,
      title: this.title,
      artist: this.artist,
      album: this.album,
      sourceType: this.sourceType,
      sourceId: this.sourceId,
      order: order
    };
  }

  static fromDto(d: PlaylistDto): Track {
    const t = new Track();
    t.id = d.id;
    t.uri = d.uri;
    t.title = d.title;
    t.artist = d.artist;
    t.album = d.album;
    t.sourceType = d.sourceType;
    t.sourceId = d.sourceId;
    return t;
  }
}
