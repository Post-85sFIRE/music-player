// 歌词服务：三层来源兜底 + 手动搜索。对齐 Windows 端 LyricsService 的优先级与编排，
// 但鸿蒙无 TagLib，本地仅做文件名匹配（不支持内嵌标签解析）。
// 优先级：本地同目录 .lrc（文件名匹配） → 云盘同目录 .lrc（GET） → LRCLIB（中文） → api.lyrics.ovh。
// 设计为只读展示，不写回本地/云盘（写回留后续）。
import { http } from '@kit.NetworkKit';
import fs from '@ohos.file.fs';
import { Track, SourceType } from './Track';
import { WebDavClient } from './WebDavClient';
import { LrcParser, LyricLine, LyricsResult } from './Lyrics';

export class LyricsService {
  private replaceExt(uri: string, ext: string): string {
    const idx = uri.lastIndexOf('.');
    if (idx < 0) {
      return uri + ext;
    }
    return uri.substring(0, idx) + ext;
  }

  private dirOf(uri: string): string {
    const idx = Math.max(uri.lastIndexOf('/'), uri.lastIndexOf('\\'));
    return idx < 0 ? '' : uri.substring(0, idx + 1);
  }

  private safeName(s: string): string {
    return s.replace(/[^a-zA-Z0-9一-龥 _\-]/g, '_');
  }

  // 本地：尝试 同名.lrc / 仅标题.lrc / 艺术家-标题.lrc
  private async loadLocal(t: Track): Promise<LyricsResult | null> {
    if (t.sourceType !== SourceType.Local || !t.uri) {
      return null;
    }
    const cands: string[] = [
      this.replaceExt(t.uri, '.lrc'),
      this.dirOf(t.uri) + this.safeName(t.title) + '.lrc',
      this.dirOf(t.uri) + this.safeName((t.artist ? t.artist + '-' : '') + t.title) + '.lrc'
    ];
    for (const c of cands) {
      try {
        if (fs.access(c)) {
          const text = fs.readTextSync(c);
          return { lines: LrcParser.parse(text), source: 'Local', raw: text };
        }
      } catch (e) {
        // 跳过不可读文件
      }
    }
    return null;
  }

  // 云盘：用 client 探测同目录 .lrc 并 GET（只读）
  private async loadCloud(t: Track, client: WebDavClient): Promise<LyricsResult | null> {
    if (t.sourceType !== SourceType.Cloud) {
      return null;
    }
    const lrcHref = this.replaceExt(t.uri, '.lrc');
    const text = await client.getText(lrcHref);
    if (text && text.length > 0) {
      return { lines: LrcParser.parse(text), source: 'Cloud', raw: text };
    }
    return null;
  }

  private async loadLrclib(artist: string, title: string): Promise<LyricsResult | null> {
    if (!artist && !title) {
      return null;
    }
    try {
      const url = `https://lrclib.net/api/get?artist=${encodeURIComponent(artist)}&track=${encodeURIComponent(title)}`;
      const req = http.createHttp();
      const resp = await req.request(url, { method: http.RequestMethod.GET, expectDataType: http.HttpDataType.STRING });
      req.destroy();
      if (resp.responseCode === 200 && typeof resp.result === 'string') {
        const json = JSON.parse(resp.result);
        const raw = json.syncedLyrics || json.plainLyrics;
        if (raw) {
          return { lines: LrcParser.parse(raw), source: 'Lrclib', raw };
        }
      }
    } catch (e) {
      // 网络失败，继续兜底
    }
    return null;
  }

  private async loadOnline(artist: string, title: string): Promise<LyricsResult | null> {
    if (!artist || !title) {
      return null;
    }
    try {
      const url = `https://api.lyrics.ovh/v1/${encodeURIComponent(artist)}/${encodeURIComponent(title)}`;
      const req = http.createHttp();
      const resp = await req.request(url, { method: http.RequestMethod.GET, expectDataType: http.HttpDataType.STRING });
      req.destroy();
      if (resp.responseCode === 200 && typeof resp.result === 'string') {
        const json = JSON.parse(resp.result);
        if (json.lyrics) {
          return { lines: LrcParser.parse(json.lyrics), source: 'Online', raw: json.lyrics };
        }
      }
    } catch (e) {
      // 网络失败
    }
    return null;
  }

  // 自动加载：按优先级兜底；manual=true 时跳过本地/云盘直接走在线源。
  async loadAsync(t: Track, onlineEnabled: boolean, cloudClient?: WebDavClient, manual: boolean = false): Promise<LyricsResult> {
    if (!manual) {
      const local = await this.loadLocal(t);
      if (local) {
        return local;
      }
      if (cloudClient) {
        const cloud = await this.loadCloud(t, cloudClient);
        if (cloud) {
          return cloud;
        }
      }
    }
    if (onlineEnabled) {
      const lr = await this.loadLrclib(t.artist, t.title);
      if (lr) {
        return lr;
      }
      const ovh = await this.loadOnline(t.artist, t.title);
      if (ovh) {
        return ovh;
      }
    }
    return { lines: [], source: '', raw: '' };
  }

  // 手动搜索：解析 "artist - title"，直接走在线源。
  async searchAsync(query: string, onlineEnabled: boolean): Promise<LyricsResult> {
    let artist = '';
    let title = '';
    const idx = query.indexOf('-');
    if (idx > 0) {
      artist = query.substring(0, idx).trim();
      title = query.substring(idx + 1).trim();
    } else {
      title = query.trim();
    }
    if (!onlineEnabled) {
      return { lines: [], source: '', raw: '' };
    }
    const lr = await this.loadLrclib(artist, title);
    if (lr) {
      return lr;
    }
    const ovh = await this.loadOnline(artist, title);
    if (ovh) {
      return ovh;
    }
    return { lines: [], source: '', raw: '' };
  }
}
