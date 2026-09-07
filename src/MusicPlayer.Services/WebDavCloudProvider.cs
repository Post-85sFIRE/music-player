using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using MusicPlayer.Core.Interfaces;
using MusicPlayer.Core.Models;

namespace MusicPlayer.Services;

/// <summary>
/// 通用 WebDAV 云源（坚果云等官方支持的 WebDAV）。HTTP Basic 鉴权；
/// PROPFIND 列目录、GET（带 Range）流式播放、GET 下载带进度。
/// </summary>
public class WebDavCloudProvider : ICloudSourceProvider
{
    private static readonly XNamespace Dav = "DAV:";
    private readonly CloudSourceConfig _cfg;
    private readonly HttpClient _http;

    public WebDavCloudProvider(CloudSourceConfig cfg)
    {
        _cfg = cfg;
        _http = new HttpClient();
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes(cfg.UserName + ":" + cfg.Password));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
    }

    public string Id => _cfg.Id;
    public string? LastError { get; private set; }

    private string FullUrl(string relative)
    {
        var baseUrl = _cfg.BaseUrl.Replace('\\', '/').TrimEnd('/');
        var rel = (relative ?? "").TrimStart('/');
        if (string.IsNullOrEmpty(rel)) return baseUrl + "/";
        // 按段编码，避免中文/特殊字符路径 404；base 不动（已为合法 URL）。
        var encoded = string.Join("/", rel.Split('/').Select(s => Uri.EscapeDataString(s)));
        return baseUrl + "/" + encoded;
    }

    public async Task<bool> LoginAsync()
    {
        LastError = null;
        try
        {
            var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), FullUrl(""));
            req.Headers.Add("Depth", "0");
            req.Content = new StringContent(PropfindBody(), Encoding.UTF8, "application/xml");
            using var resp = await _http.SendAsync(req);
            if (resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.MultiStatus)
                return true;

            LastError = MapLoginError(resp.StatusCode);
            Console.Error.WriteLine($"[WebDav] {LastError}");
            return false;
        }
        catch (Exception ex)
        {
            LastError = "连接异常：" + ex.Message;
            Console.Error.WriteLine($"[WebDav] {LastError}");
            return false;
        }
    }

    private static string MapLoginError(System.Net.HttpStatusCode code) => code switch
    {
        System.Net.HttpStatusCode.Unauthorized => "401 未授权：用户名或应用密码错误（注意不是坚果云登录密码，而是第三方应用密码）。",
        System.Net.HttpStatusCode.NotFound => "404 目录不存在：请确认 URL 路径正确，且 WebDAV 根是 https://dav.jianguoyun.com/dav/（常见错误是多加\"我的坚果云\"这一层）。",
        System.Net.HttpStatusCode.Forbidden => "403 无权限：请检查应用密码是否有权访问该目录。",
        System.Net.HttpStatusCode.PaymentRequired => "402 请求受限：坚果云可能限制了该应用的访问。",
        _ => $"连接失败：HTTP {(int)code} ({code})"
    };

    public async Task<IEnumerable<CloudEntry>> BrowseAsync(string folderId, CancellationToken ct)
    {
        var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), FullUrl(folderId));
        req.Headers.Add("Depth", "1");
        req.Content = new StringContent(PropfindBody(), Encoding.UTF8, "application/xml");
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        var xml = await resp.Content.ReadAsStringAsync(ct);
        return ParseMultistatus(xml, folderId);
    }

    public async Task<Stream> OpenStreamAsync(string fileId, long? fromByte, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, FullUrl(fileId));
        if (fromByte is > 0)
            req.Headers.Range = new RangeHeaderValue(fromByte, null);
        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStreamAsync(ct);
    }

    public async Task<long> DownloadAsync(string fileId, string destPath, IProgress<double>? progress, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, FullUrl(fileId));
        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(destPath);
        var buf = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;
            if (total > 0) progress?.Report((double)read / total);
        }
        return read;
    }

    public async Task UploadAsync(string fileId, Stream content, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, FullUrl(fileId));
        await using (content)
        {
            req.Content = new StreamContent(content);
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
        }
    }

    public async Task<string?> GetTextAsync(string fileId, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, FullUrl(fileId));
        using var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync(ct);
    }

    public async Task EnsureFolderAsync(string folderId, CancellationToken ct)
    {
        var req = new HttpRequestMessage(new HttpMethod("MKCOL"), FullUrl(folderId));
        using var resp = await _http.SendAsync(req, ct);
        // 201 已创建；405 已存在(Method Not Allowed)；409 父不存在或已存在(Node Already Exists)——均按"目录可继续"处理。
        if (resp.IsSuccessStatusCode
            || resp.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed
            || resp.StatusCode == System.Net.HttpStatusCode.Conflict
            || resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
            return;
        resp.EnsureSuccessStatusCode();
    }

    private static string PropfindBody() =>
        "<?xml version=\"1.0\"?><D:propfind xmlns:D=\"DAV:\"><D:prop>" +
        "<D:resourcetype/><D:getcontentlength/><D:displayname/></D:prop></D:propfind>";

    private static readonly HashSet<string> MusicExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".ape", ".wav", ".ogg", ".oga", ".m4a", ".aac", ".opus", ".wma", ".dsf", ".dff"
    };

    private IEnumerable<CloudEntry> ParseMultistatus(string xml, string folderId)
    {
        var doc = XDocument.Parse(xml);

        // 计算 WebDAV 根路径，例如 https://dav.jianguoyun.com/dav/ -> /dav/
        var baseUri = new Uri(_cfg.BaseUrl);
        var basePath = baseUri.AbsolutePath;
        if (!basePath.StartsWith('/')) basePath = "/" + basePath;
        if (!basePath.EndsWith('/')) basePath += "/";

        // 当前请求的绝对路径（用于准确跳过自身条目，解决根目录出现假 "dav" 文件夹的问题）
        var requested = (basePath.TrimEnd('/') + "/" + (folderId ?? "").TrimStart('/')).TrimEnd('/');

        foreach (var resp in doc.Descendants(Dav + "response"))
        {
            var href = resp.Element(Dav + "href")?.Value ?? "";
            var path = Uri.UnescapeDataString(href).TrimEnd('/');

            // 跳过自身（目录的 href 等于请求路径）
            if (string.Equals(path.TrimEnd('/'), requested, StringComparison.OrdinalIgnoreCase))
                continue;

            var propstat = resp.Element(Dav + "propstat")?.Element(Dav + "prop");
            if (propstat is null) continue;

            var isFolder = propstat.Element(Dav + "resourcetype")?.Element(Dav + "collection") != null;
            var name = Path.GetFileName(path.TrimEnd('/'));
            if (string.IsNullOrEmpty(name)) name = path;
            var size = long.TryParse(propstat.Element(Dav + "getcontentlength")?.Value, out var s) ? s : 0;
            var ext = Path.GetExtension(name);
            var isMusic = !isFolder && MusicExtensions.Contains(ext);

            // Id 应该是相对于 WebDAV 根的路径，避免请求时拼成 /dav/dav/... 导致 409
            var id = path;
            if (path.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                id = path.Substring(basePath.Length).TrimStart('/');

            yield return new CloudEntry { Id = id, Name = name, IsFolder = isFolder, Size = size, IsMusic = isMusic };
        }
    }
}
