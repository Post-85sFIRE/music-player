using System;
using System.Runtime.InteropServices;

namespace MusicPlayer.Audio;

/// <summary>
/// BASS WASAPI add-on 的轻量 P/Invoke 包装（NuGet 的 Un4seen.Bass 不含该 add-on 封装）。
/// 仅声明 M1.x 独占输出所需的最小集合；basswasapi.dll 由 tools/fetch-bass.ps1 下载放入 exe 目录。
/// 运行期若缺少 basswasapi.dll，调用会抛 DllNotFoundException，由 BassRuntime 捕获并回退共享输出。
/// </summary>
internal enum BassWasapiInit
{
    Exclusive = 0x1,
    Autoformat = 0x2,
    Mixer = 0x4,
    Stream = 0x8,
    Event = 0x10,
}

internal enum BassWasapiVolume
{
    /// <summary>会话音量（系统混音器里该会话的音量）。</summary>
    Session = 0x0,
    /// <summary>流音量（本程序推给 WASAPI 的数据增益，独占模式下用它调音量最稳）。</summary>
    Stream = 0x1,
    /// <summary>设备音量（影响整个物理设备，权限较高，通常不推荐应用层设置）。</summary>
    Device = 0x2,
}

internal static class BassWasapi
{
    private const string Dll = "basswasapi";

    /// <summary>原生 basswasapi.dll 是否可用（不可用时独占输出不可用）。</summary>
    public static bool IsAvailable
    {
        get
        {
            try { return GetVersion() != 0; }
            catch (DllNotFoundException) { return false; }
        }
    }

    public static int GetVersion() => BASS_WASAPI_GetVersion();

    /// <summary>
    /// 初始化 WASAPI 设备。M1.x 使用 Exclusive|Stream|Autoformat：
    /// Stream 模式让 BASS 解码流直接走 WASAPI 独占输出；Autoformat 允许格式协商。
    /// device=-1 取默认设备；freq/chans=0 由 BASS 采用默认设备格式。
    /// </summary>
    public static bool Init(int device, int freq, int chans, BassWasapiInit flags, float buffer, float period)
    {
        var ok = BASS_WASAPI_Init(device, freq, chans, flags, buffer, period, IntPtr.Zero, IntPtr.Zero);
        Diagnostics.Write($"BASS_WASAPI_Init(device={device}, freq={freq}, chans={chans}, flags={flags}) => {ok}");
        return ok;
    }

    public static bool Free() => BASS_WASAPI_Free();

    /// <summary>
    /// 启动 WASAPI 设备输出。独占/共享模式在 <see cref="ChannelPlay"/> 之后都应确保设备已 Start，
    /// 否则会出现"初始化成功却不出声"。官方文档说明 ChannelPlay 与 Start 等价（会自动起设备），
    /// 这里仍显式补一次 Start 作为兜底：设备已在运行则其为幂等 no-op，不会造成副作用。
    /// </summary>
    public static bool Start()
    {
        var ok = BASS_WASAPI_Start();
        Diagnostics.Write($"BASS_WASAPI_Start() => {ok}");
        return ok;
    }

    /// <summary>
    /// 把一条 BASS 解码流交给 WASAPI 设备播放（独占/共享皆可）。
    /// restart=false 表示从当前位置续播（用于暂停后恢复）；true 表示从头播放。
    /// 注意：传入的流必须是解码流（BASS_STREAM_DECODE），由 WASAPI 负责拉取数据并输出。
    /// </summary>
    public static bool ChannelPlay(int channel, bool restart)
    {
        // 关键：原生 BOOL 是 4 字节，C# bool 默认按 1 字节封送会传错 restart 参数（甚至破坏调用），
        // 这里显式用 int 传 0/1，避免独占播放因参数错乱而失败。
        var ok = BASS_WASAPI_ChannelPlay(channel, restart ? 1 : 0);
        Diagnostics.Write($"BASS_WASAPI_ChannelPlay(channel={channel}, restart={restart}) => {ok}");
        return ok;
    }

    /// <summary>暂停 WASAPI 正在播放的解码流（停止数据拉取，等效于暂停）。</summary>
    public static bool ChannelPause(int channel) => BASS_WASAPI_ChannelPause(channel);

    /// <summary>停止 WASAPI 解码流播放。</summary>
    public static bool ChannelStop(int channel) => BASS_WASAPI_ChannelStop(channel);

    /// <summary>设置 WASAPI 音量（按 <see cref="BassWasapiVolume"/> 选择作用域）。返回设置后的实际音量，失败返回负值。</summary>
    public static float SetVolume(BassWasapiVolume volume, float value) => BASS_WASAPI_SetVolume(volume, value);

    [DllImport(Dll, EntryPoint = "BASS_WASAPI_GetVersion")]
    private static extern int BASS_WASAPI_GetVersion();

    [DllImport(Dll, EntryPoint = "BASS_WASAPI_Init")]
    private static extern bool BASS_WASAPI_Init(
        int device, int freq, int chans, BassWasapiInit flags,
        float buffer, float period, IntPtr proc, IntPtr user);

    [DllImport(Dll, EntryPoint = "BASS_WASAPI_Free")]
    private static extern bool BASS_WASAPI_Free();

    [DllImport(Dll, EntryPoint = "BASS_WASAPI_Start")]
    private static extern bool BASS_WASAPI_Start();

    [DllImport(Dll, EntryPoint = "BASS_WASAPI_ChannelPlay")]
    private static extern bool BASS_WASAPI_ChannelPlay(int channel, int restart);

    [DllImport(Dll, EntryPoint = "BASS_WASAPI_ChannelPause")]
    private static extern bool BASS_WASAPI_ChannelPause(int channel);

    [DllImport(Dll, EntryPoint = "BASS_WASAPI_ChannelStop")]
    private static extern bool BASS_WASAPI_ChannelStop(int channel);

    [DllImport(Dll, EntryPoint = "BASS_WASAPI_SetVolume")]
    private static extern float BASS_WASAPI_SetVolume(BassWasapiVolume volume, float value);
}
