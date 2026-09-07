// 本地音乐扫描：通过 AudioViewPicker 免权限选取音频文件（返回 file:// URI）。
// 与 Windows 端"扫描本地目录"等价，但鸿蒙 Next 下 AudioViewPicker 是最稳妥、无需授权的方式。
import picker from '@ohos.file.picker';
import { Track, SourceType } from '../model/Track';

export class LibraryScanner {
  async pickAudio(): Promise<Track[]> {
    const avPicker = new picker.AudioViewPicker();
    // API 22 的 AudioSelectOptions 无 MIMEType 字段，AudioMIMEType 亦不存在；保留 maxSelectNumber 即可。
    const options: picker.AudioSelectOptions = {
      maxSelectNumber: 200
    };
    try {
      const uris: string[] = await avPicker.select(options);
      return uris.map((u: string) => {
        return Track.fromLocal(u, LibraryScanner.nameFromUri(u));
      });
    } catch (e) {
      return [];
    }
  }

  private static nameFromUri(uri: string): string {
    const parts: string[] = uri.split('/');
    const last: string = parts[parts.length - 1] || '未知曲目';
    const dot: number = last.lastIndexOf('.');
    return dot > 0 ? last.substring(0, dot) : last;
  }
}
