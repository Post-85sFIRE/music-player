// 设备级加密存储：用 @ohos.security.asset 把云盘密码存为设备级密文，
// 避免明文落在 preferences。alias 以 cloudSource.id 派生，读写删均按 alias。
// 注：Asset 接口以 Map<asset.Tag, asset.ValueType> 传参；本机 DevEco 编译时若类型名有出入，以官方文档为准微调。
import asset from '@ohos.security.asset';

export class SecureStore {
  private static enc(s: string): Uint8Array {
    const arr = new Uint8Array(s.length);
    for (let i = 0; i < s.length; i++) {
      arr[i] = s.charCodeAt(i) & 0xff;
    }
    return arr;
  }

  // 注：API 22 下 @ohos.security.asset 默认导出里没有 ValueType 这个命名成员，
  // 本类只用 Tag.ALIAS / Tag.SECRET 且均为 Uint8Array，故直接用 Uint8Array 作 map value 类型。

  private static dec(b: Uint8Array | undefined): string {
    if (!b) {
      return '';
    }
    let s = '';
    for (let i = 0; i < b.length; i++) {
      s += String.fromCharCode(b[i]);
    }
    return s;
  }

  static async saveSecret(alias: string, value: string): Promise<void> {
    const map = new Map<asset.Tag, Uint8Array>();
    map.set(asset.Tag.ALIAS, this.enc(alias));
    map.set(asset.Tag.SECRET, this.enc(value));
    try {
      await asset.remove(map);
    } catch (e) {
      // 不存在则忽略
    }
    await asset.add(map);
  }

  static async getSecret(alias: string): Promise<string | undefined> {
    const map = new Map<asset.Tag, Uint8Array>();
    map.set(asset.Tag.ALIAS, this.enc(alias));
    try {
      const res: asset.AssetMap[] = await asset.query(map);
      if (res.length > 0) {
        const secret = res[0].get(asset.Tag.SECRET) as Uint8Array | undefined;
        if (secret && secret.length > 0) {
          return this.dec(secret);
        }
      }
    } catch (e) {
      // 查询失败返回 undefined
    }
    return undefined;
  }

  static async deleteSecret(alias: string): Promise<void> {
    const map = new Map<asset.Tag, Uint8Array>();
    map.set(asset.Tag.ALIAS, this.enc(alias));
    try {
      await asset.remove(map);
    } catch (e) {
      // 忽略
    }
  }
}
