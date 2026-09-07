// 播放列表（队列）管理：增删、当前项、上一首/下一首（含播放模式）、拖拽排序、清空、持久化。
import { Track } from '../model/Track';
import { PlayMode } from '../model/PlayerEngine';
import { PlaylistDto } from '../model/Settings';

export class PlaylistManager {
  private items: Track[] = [];
  private index: number = -1;

  get all(): Track[] {
    return this.items;
  }

  get current(): Track | null {
    if (this.index >= 0 && this.index < this.items.length) {
      return this.items[this.index];
    }
    return null;
  }

  get currentIndex(): number {
    return this.index;
  }

  addAll(tracks: Track[]): void {
    for (const t of tracks) {
      if (!this.items.find((i) => i.id === t.id)) {
        this.items.push(t);
      }
    }
  }

  remove(id: string): void {
    const idx: number = this.items.findIndex((i) => i.id === id);
    if (idx >= 0) {
      this.items.splice(idx, 1);
      if (this.index >= this.items.length) {
        this.index = this.items.length - 1;
      }
    }
  }

  removeSelected(ids: string[]): void {
    for (const id of ids) {
      this.remove(id);
    }
  }

  clear(): void {
    this.items = [];
    this.index = -1;
  }

  // 拖拽排序：把 from 位置的项移到 to 位置
  reorder(from: number, to: number): void {
    if (from < 0 || from >= this.items.length || to < 0 || to >= this.items.length || from === to) {
      return;
    }
    const [moved] = this.items.splice(from, 1);
    this.items.splice(to, 0, moved);
    // 重新对齐当前播放项索引
    const cur = this.current;
    this.index = cur ? this.items.indexOf(cur) : -1;
  }

  select(index: number): Track | null {
    if (index < 0 || index >= this.items.length) {
      return null;
    }
    this.index = index;
    return this.items[index];
  }

  next(mode: PlayMode): Track | null {
    if (this.items.length === 0) {
      return null;
    }
    if (mode === PlayMode.Shuffle) {
      this.index = Math.floor(Math.random() * this.items.length);
      return this.items[this.index];
    }
    let n: number = this.index + 1;
    if (n >= this.items.length) {
      n = 0;
    }
    this.index = n;
    return this.items[n];
  }

  prev(mode: PlayMode): Track | null {
    if (this.items.length === 0) {
      return null;
    }
    if (mode === PlayMode.Shuffle) {
      this.index = Math.floor(Math.random() * this.items.length);
      return this.items[this.index];
    }
    let p: number = this.index - 1;
    if (p < 0) {
      p = this.items.length - 1;
    }
    this.index = p;
    return this.items[p];
  }

  // 持久化恢复：按 order 排序重建；保留当前项引用
  restore(dtos: PlaylistDto[]): void {
    const sorted = dtos.slice().sort((a, b) => a.order - b.order);
    this.items = sorted.map((d) => Track.fromDto(d));
    this.index = this.items.length > 0 ? 0 : -1;
  }

  toDtos(): PlaylistDto[] {
    return this.items.map((t, i) => t.toDto(i));
  }
}
