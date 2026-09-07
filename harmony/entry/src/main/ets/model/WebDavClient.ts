// 云盘 WebDAV 客户端：HTTP Basic 鉴权 + PROPFIND 列目录 + GET 流式缓存到沙箱。
// 设计对齐 Windows 端 WebDavCloudProvider / DownloadCacheService：云文件先缓存为本地文件，再交给 AVPlayer 播放。
//
// ⚠️ API 22 适配说明：
//  - WebDAV 的 PROPFIND 不是标准 HTTP 方法。http 模块的 RequestMethod 枚举不含 PROPFIND，
//    把 method 断言成 'PROPFIND' 会被运行时拒绝（401 Parameter error）。正确做法是用 customMethod:'PROPFIND'。
//    但官方示例中**不同时设 method 字段**；若设 method:GET + customMethod:PROPFIND，extraData 会被当成 URL
//    查询参数拼接到地址上，触发 2300003 URL 非法。因此只设 customMethod 并整体断言为 HttpRequestOptions。
//  - fs.access / fs.stat 在 API 22 改为返回 Promise（异步），此处均已 await。
//  - 进度事件回调类型为 http.DataReceiveProgressInfo，字段 receiveSize / totalSize。
import { http } from '@kit.NetworkKit';
import fs from '@ohos.file.fs';
import util from '@ohos.util';
import { common } from '@kit.AbilityKit';

export interface CloudEntry {
  id: string;      // 条目 href（绝对 URL）
  name: string;
  isFolder: boolean;
  size: number;
  isCached: boolean; // 是否已缓存到沙箱（云盘列表标记用）
}

const PROPFIND_BODY: string =
  '<?xml version="1.0" encoding="utf-8"?>' +
  '<D:propfind xmlns:D="DAV:">' +
  '<D:prop><D:displayname/><D:resourcetype/><D:getcontentlength/><D:getcontenttype/></D:prop>' +
  '</D:propfind>';

export class WebDavClient {
  private base64: util.Base64Helper = new util.Base64Helper();

  constructor(
    private baseUrl: string,
    private user: string,
    private password: string,
    private context: common.UIAbilityContext
  ) {
  }

  private authHeader(): Record<string, string> {
    const raw: string = this.user + ':' + this.password;
    const bytes: Uint8Array = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) {
      bytes[i] = raw.charCodeAt(i) & 0xff;
    }
    return { 'Authorization': 'Basic ' + this.base64.encodeToString(bytes) };
  }

  private joinUrl(base: string, path: string): string {
    if (!path) {
      return base.endsWith('/') ? base : base + '/';
    }
    const b: string = base.endsWith('/') ? base : base + '/';
    const p: string = path.startsWith('/') ? path.substring(1) : path;
    return b + p;
  }

  // 发起 PROPFIND 请求。
  // ⚠️ 本 SDK 的 HttpRequestOptions TS 类型未声明 customMethod，但实际运行时支持（官方 WebDAV 示例）。
  //    因 Promise 重载在 customMethod 上表现异常，这里改用 callback 重载并包装为 Promise。
  //    同时 extraData 配合 customMethod 仍有问题，登录自检 Depth:0 先不带 body；列目录 Depth:1 同样不带 body，
  //    靠服务器返回默认属性（含 displayname / resourcetype / getcontentlength）。
  private async propfind(url: string, depth: string, body: string): Promise<{ ok: boolean; xml?: string; status?: number; error?: string }> {
    const req: http.HttpRequest = http.createHttp();
    return new Promise((resolve) => {
      req.request(url, {
        customMethod: 'PROPFIND',
        header: { ...this.authHeader(), 'Content-Type': 'text/xml; charset=utf-8', 'Depth': depth },
        expectDataType: http.HttpDataType.STRING
      } as unknown as http.HttpRequestOptions, (err: Error, data: http.HttpResponse) => {
        req.destroy();
        if (err) {
          resolve({ ok: false, error: JSON.stringify(err) + ' (url=' + url + ')' });
          return;
        }
        if (data.responseCode === 207 || data.responseCode === 200) {
          resolve({ ok: true, xml: typeof data.result === 'string' ? data.result : '', status: data.responseCode });
        } else {
          resolve({ ok: false, status: data.responseCode });
        }
      });
    });
  }

  // 连接自检：对根做 PROPFIND。
  async login(): Promise<{ ok: boolean; error?: string }> {
    const url: string = this.joinUrl(this.baseUrl, '');
    const result = await this.propfind(url, '0', PROPFIND_BODY);
    if (result.ok) {
      return { ok: true };
    }
    return { ok: false, error: result.error || (result.status ? `HTTP ${result.status}` : 'PROPFIND 失败') };
  }

  // 列出某目录下的子项（Depth:1 PROPFIND），自动剔除"自身"那一条。
  async list(folder: string): Promise<CloudEntry[]> {
    const url: string = this.joinUrl(this.baseUrl, folder);
    const result = await this.propfind(url, '1', PROPFIND_BODY);
    if (!result.ok || !result.xml) {
      return [];
    }
    const all: CloudEntry[] = this.parseMultistatus(result.xml);
    const self: string = url.replace(/\/$/, '');
    return all.filter((e) => e.id.replace(/\/$/, '') !== self && e.id.length > 0);
  }

  // 流式下载到沙箱缓存目录，返回 file:// 路径供 AVPlayer 播放；已缓存则直接命中。
  async cacheAndGetLocal(entryId: string, onProgress?: (received: number, total: number) => void): Promise<string> {
    const dir: string = this.context.cacheDir + '/webdav';
    await this.ensureDir(dir);
    const localPath: string = dir + '/' + this.safeName(entryId) + '.cache';
    try {
      if (await fs.access(localPath)) {
        const stat: fs.Stat = await fs.stat(localPath);
        if (stat.size > 0) {
          return 'file://' + localPath;
        }
      }
    } catch (e) {
      // 文件不存在，继续下载
    }

    const file: fs.File = fs.openSync(localPath, fs.OpenMode.CREATE | fs.OpenMode.WRITE_ONLY | fs.OpenMode.TRUNC);
    const req: http.HttpRequest = http.createHttp();
    req.on('dataReceive', (chunk: ArrayBuffer) => {
      fs.writeSync(file.fd, chunk);
    });
    req.on('dataReceiveProgress', (p: http.DataReceiveProgressInfo) => {
      onProgress?.(p.receiveSize, p.totalSize);
    });
    await req.request(this.joinUrl(this.baseUrl, entryId), {
      method: http.RequestMethod.GET,
      header: this.authHeader(),
      expectDataType: http.HttpDataType.ARRAY_BUFFER
    });
    req.destroy();
    fs.closeSync(file.fd);
    return 'file://' + localPath;
  }

  // 取文本（用于云盘 .lrc 歌词），不存在/失败返回 null（只读，不写回）。
  async getText(entryId: string): Promise<string | null> {
    try {
      const req: http.HttpRequest = http.createHttp();
      const resp: http.HttpResponse = await req.request(this.joinUrl(this.baseUrl, entryId), {
        method: http.RequestMethod.GET,
        header: this.authHeader(),
        expectDataType: http.HttpDataType.STRING
      });
      req.destroy();
      if (resp.responseCode === 200 && typeof resp.result === 'string') {
        return resp.result;
      }
    } catch (e) {
      // 404 或其它错误
    }
    return null;
  }

  // 是否已缓存（复用落盘规则：cacheDir/webdav/<safeName>.cache 存在且 size>0）
  async isCached(entryId: string): Promise<boolean> {
    const localPath: string = this.context.cacheDir + '/webdav/' + this.safeName(entryId) + '.cache';
    try {
      if (await fs.access(localPath)) {
        const stat: fs.Stat = await fs.stat(localPath);
        return stat.size > 0;
      }
    } catch (e) {
      // 文件不存在
    }
    return false;
  }

  private parseMultistatus(xml: string): CloudEntry[] {
    const entries: CloudEntry[] = [];
    const re: RegExp = /<response>([\s\S]*?)<\/response>/g;
    let m: RegExpExecArray | null;
    while ((m = re.exec(xml)) !== null) {
      const block: string = m[1];
      const href: string | undefined = this.matchTag(block, 'href');
      if (!href) {
        continue;
      }
      const isFolder: boolean = /<collection[\s/]/.test(block);
      const name: string = this.matchTag(block, 'displayname')?.trim() || this.nameFromHref(href);
      const sizeStr: string = this.matchTag(block, 'getcontentlength')?.trim() || '0';
      const size: number = parseInt(sizeStr, 10) || 0;
      entries.push({ id: href.trim(), name, isFolder, size, isCached: false });
    }
    return entries;
  }

  private matchTag(block: string, tag: string): string | undefined {
    const re: RegExp = new RegExp('<[^:]*:' + tag + '[^>]*>([\\s\\S]*?)<\\/[^:]*:' + tag + '>', 'i');
    const mm: RegExpExecArray | null = re.exec(block);
    return mm ? mm[1] : undefined;
  }

  private nameFromHref(href: string): string {
    const decoded: string = href.split('/').pop() || href;
    return decoded;
  }

  private safeName(s: string): string {
    return s.replace(/[^a-zA-Z0-9]/g, '_');
  }

  private async ensureDir(dir: string): Promise<void> {
    try {
      if (!(await fs.access(dir))) {
        fs.mkdir(dir);
      }
    } catch (e) {
      // 已存在或创建失败，交给上层
    }
  }
}
