using System;
using System.Collections.Generic;

namespace MusicPlayer.Core.Models;

/// <summary>已配置的云盘来源（持久化在 AppSettings.CloudSources，随设置 JSON 落库，无需新建表）。</summary>
public class CloudSourceConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProviderType { get; set; } = "WebDav"; // 当前仅 "WebDav"（坚果云 WebDAV）
    public string Name { get; set; } = "";
    public string BaseUrl { get; set; } = "";   // WebDAV 根，如 https://dav.jianguoyun.com/dav/
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";  // 坚果云为"应用密码"
}
