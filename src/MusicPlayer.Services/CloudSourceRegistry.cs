using System.Collections.Generic;
using MusicPlayer.Core.Interfaces;

namespace MusicPlayer.Services;

/// <summary>
/// 云源注册表（实装：坚果云 WebDAV；通用 WebDAV 协议，可接任意 WebDAV 源）。
/// 第一期仅定义容器，UI 的"来源切换"只依赖 ICloudSourceProvider 接口。
/// </summary>
public class CloudSourceRegistry
{
    private readonly List<ICloudSourceProvider> _providers = new();

    public void Register(ICloudSourceProvider provider) => _providers.Add(provider);
    public IReadOnlyList<ICloudSourceProvider> All => _providers;
    public ICloudSourceProvider? Get(string id) => _providers.Find(p => p.Id == id);
    public void Unregister(string id) => _providers.RemoveAll(p => p.Id == id);
}
