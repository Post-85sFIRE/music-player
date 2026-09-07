using System;
using System.Threading;
using Un4seen.Bass;

namespace MusicPlayer.Audio;

/// <summary>
/// BASS 运行时：加载解码插件、初始化输出设备。
/// M1 默认走 BASS 标准输出（Windows 即 WASAPI 共享模式）。
/// M1.x 支持 WASAPI 独占输出（bit-perfect，绕过系统混音器重采样）：当 ExclusiveMode 为 true
/// 且原生 basswasapi.dll 可用时，调用 BASS_WASAPI_Init(EXCLUSIVE|STREAM|AUTOFORMAT)，
/// 之后 BASS 流直接走 WASAPI 独占；任何失败都回退到共享输出，保证出声。
/// </summary>
public class BassRuntime : IDisposable
{
    private bool _initialized;
    private bool _usingExclusive;
    private bool _usingNoSoundDevice;
    private string? _lastError;

    /// <summary>是否尝试 WASAPI 独占输出（来自 AppSettings.ExclusiveWasapi）。</summary>
    public bool ExclusiveMode { get; set; }

    /// <summary>
    /// WASAPI 独占初始化超时（毫秒）。超时即放弃独占、回退共享输出，
    /// 避免 BASS_WASAPI_Init 挂住导致启动卡死。
    /// </summary>
    public int ExclusiveInitTimeoutMs { get; set; } = 3000;

    /// <summary>最近一次初始化失败的原因（如缺少原生 bass.dll）。供 UI 给出明确中文提示。</summary>
    public string? LastError => _lastError;

    /// <summary>当前是否实际处于 WASAPI 独占模式（用于诊断展示）。</summary>
    public bool IsExclusive => _usingExclusive;

    /// <summary>
    /// 实际生效的输出模式描述（如"WASAPI 独占"/"默认设备/44100Hz"/"无输出设备(保底，不发声)"）。
    /// 用于诊断与 UI 展示，让用户一眼看出当前是否真的能出声。
    /// </summary>
    public string? OutputDescription { get; private set; }

    /// <summary>
    /// 当前是否退到了 no-sound 设备。此时 BASS 全流程可跑通但**不会发声**，
    /// UI 必须明确提示，否则用户会以为程序坏了。
    /// </summary>
    public bool UsingNoSoundDevice => _usingNoSoundDevice;

    /// <summary>
    /// 初始化结果：供上层判断"成功 / 独占 / 退到无声保底 / 失败原因"，决定如何提示用户。
    /// </summary>
    public sealed record BassInitResult(bool Success, bool Exclusive, bool NoSound, string? Description, string? Error);

    private BassInitResult _lastResult = new(false, false, false, null, null);

    /// <summary>
    /// 初始化音频输出（仅首次有效；重复调用直接返回上一次结果）。
    /// 独占模式由 <see cref="ExclusiveMode"/> 决定；独占失败会自动回退共享/默认设备。
    /// </summary>
    public BassInitResult Initialize(bool tryExclusive)
    {
        if (_initialized) return _lastResult;

        // 逐步骤落盘：BASS_Init 在部分环境会静默挂起，只有看到最后一行才知道卡在哪。
        _lastResult = DoInitialize(tryExclusive);
        return _lastResult;
    }

    /// <summary>
    /// 运行时重新初始化（切换 WASAPI 独占开关时调用，无需重启程序）。
    /// 会先释放旧的 WASAPI/BASS 设备，再完整重跑初始化 + 回退链，
    /// 因此无论当前是独占还是共享，都能安全切到新模式并自动回退。
    /// </summary>
    public BassInitResult Reinitialize(bool tryExclusive)
    {
        // 释放旧状态：独占模式下 BASS 也以 device=0 初始化过解码核心，必须 WASAPI 与 BASS 一并释放；
        // 共享模式只释放 BASS 即可。
        if (_initialized)
        {
            try
            {
                if (_usingExclusive)
                {
                    BassWasapi.Free();
                    try { Bass.BASS_Free(); }
                    catch { /* 解码核心可能未起来，忽略 */ }
                }
                else
                {
                    Bass.BASS_Free();
                }
            }
            catch { /* 状态本来就可能不对，忽略 */ }
        }

        // 复位全部状态，让 DoInitialize 从零开始跑完整链路（含独占→共享回退）。
        _initialized = false;
        _usingExclusive = false;
        _usingNoSoundDevice = false;
        _lastError = null;
        _lastResult = DoInitialize(tryExclusive);
        return _lastResult;
    }

    /// <summary>实际执行初始化 + 回退链；返回结构化结果。</summary>
    private BassInitResult DoInitialize(bool tryExclusive)
    {
        Diagnostics.Write($"BassRuntime.DoInitialize 开始 (tryExclusive={tryExclusive}, ExclusiveMode={ExclusiveMode})");

        try
        {
            // 两种模式都需要解码插件（FLAC/Opus/APE/ALAC 等），先加载。
            Diagnostics.Write("准备加载解码插件...");
            LoadPlugins();
            Diagnostics.Write("解码插件加载完成");

            // M1.x：尝试 WASAPI 独占输出（bit-perfect）。
            var wasapiAvailable = BassWasapi.IsAvailable;
            Diagnostics.Write($"BassWasapi.IsAvailable = {wasapiAvailable}");

            if (tryExclusive && ExclusiveMode && wasapiAvailable)
            {
                // 独占初始化**必须**带超时：设备被其它程序占用 / 虚拟声卡不支持独占时，
                // BASS_WASAPI_Init 会长时间挂起，直接把启动链路卡死（界面都出不来）。
                Diagnostics.Write($"准备调用 BASS_WASAPI_Init(独占, 超时 {ExclusiveInitTimeoutMs}ms)...");
                if (TryInitWasapiExclusive(ExclusiveInitTimeoutMs))
                {
                    // 独占 WASAPI 设备已就绪。但 WASAPI 只负责「输出」，解码仍由 BASS 核心完成——
                    // BASS_WASAPI_ChannelPlay 喂给 WASAPI 设备的是一条 BASS 解码流，因此必须先用 device=0
                    // （无输出、仅解码的 NoSound 设备）初始化 BASS 核心。否则后面 BASS_StreamCreateFile 会
                    // 直接回错误码 8（BASS_ERROR_INIT），表现就是"独占初始化成功却根本播不出来"。
                    // 这是独占模式能出声的前提，也是上一版"独占还是不能用"的真正根因。
                    // 注意：这里**不能**把 _usingNoSoundDevice 置真——真实输出是 WASAPI，NoSound 设备只是给 BASS 当解码壳。
                    Diagnostics.Write("独占设备就绪，初始化 BASS 解码核心(device=0, 仅解码)...");
                    var bassReady = Bass.BASS_Init(0, 44100, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero);
                    var bassCode = (int)Bass.BASS_ErrorGetCode();
                    Diagnostics.Write($"BASS_Init(0) 解码核心返回 {bassReady}, 错误码 {bassCode}");
                    if (bassReady)
                    {
                        _initialized = true;
                        _usingExclusive = true;
                        OutputDescription = "WASAPI 独占(bit-perfect)";
                        Console.WriteLine("[BassRuntime] WASAPI 独占输出已启用（bit-perfect）。");
                        Diagnostics.Write("独占输出已启用（WASAPI 独占设备 + BASS 解码核心均就绪）");
                        return new BassInitResult(true, true, false, OutputDescription, null);
                    }

                    // BASS 解码核心起不来：独占模式无意义，释放 WASAPI 设备并回退共享输出。
                    Diagnostics.Write($"BASS 解码核心初始化失败({bassCode})，独占不可用，回退共享输出");
                    Console.Error.WriteLine("[BassRuntime] WASAPI 设备就绪但 BASS 解码核心失败，回退共享输出。");
                    try { BassWasapi.Free(); } catch { }
                }

                Console.Error.WriteLine("[BassRuntime] WASAPI 独占初始化失败或超时，回退共享输出。");
                Diagnostics.Write("BASS_WASAPI_Init 失败/超时，准备回退共享输出");
                // 关键：独占 Init 失败可能让 BASS 内部留有半初始化状态，必须 Free 再 BASS_Init，
                // 否则下面那个共享初始化会被错误码 BASS_ERROR_ALREADY(14) 直接挡回来，
                // _initialized 仍为 false 但用户看不到任何线索。
                try { BassWasapi.Free(); } catch { /* 忽略：状态本来就不对 */ }
            }

            // 默认 / 回退：BASS 标准输出（Windows 即 WASAPI 共享模式）。
            // 若前面留有半初始化状态，先强制释放一次。
            try { Bass.BASS_SetConfig(BASSConfig.BASS_CONFIG_UPDATEPERIOD, 20); } catch { }
            Diagnostics.Write("准备调用 BASS_Init(-1, 44100, DEVICE_DEFAULT)...");
            _initialized = Bass.BASS_Init(-1, 44100, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero);
            var firstCode = (int)Bass.BASS_ErrorGetCode();
            Diagnostics.Write($"BASS_Init 返回 {_initialized}, BASS_ErrorGetCode = {firstCode}");
            if (_initialized)
            {
                OutputDescription = "默认设备/44100Hz";
                Diagnostics.Write("BASS_Init 成功：默认设备/44100Hz");
            }
            if (!_initialized)
            {
                var errCode = firstCode;
                _lastError = $"BASS 共享初始化失败（错误码 {errCode}）：{ResolveBassErrorText(errCode)}。" +
                             $"请确认系统音频服务（Windows Audio）已启动，且默认输出设备可用。" +
                             $"(raw: BASS_Init -> {errCode})";
                Console.Error.WriteLine($"[BassRuntime] {_lastError}");
                Diagnostics.Write($"共享初始化失败: {_lastError}");

                // 兜底重试前先 Free 一次：之前的失败尝试可能在 BASS 内部留了半初始化状态，
                // 不清理会让后续 Init 一律返回 BASS_ERROR_ALREADY(14)。
                try { Bass.BASS_Free(); Diagnostics.Write("Bass.BASS_Free() 已调用"); } catch { }

                // 兜底重试：BASS 的设备编号里 -1=默认设备、0=**无输出设备**(no sound)、1=第一个真实设备。
                // 所以回退顺序必须是 1（真实设备）优先，最后才用 0——直接退到 0 会"初始化成功却没声音"。
                if (errCode != 0)
                {
                    // 二次：默认设备换 48000Hz（部分声卡只接受该采样率）
                    if (TryBassInit(-1, 48000, "默认设备/48000Hz")) { _lastError = null; }
                    // 三次：第一个真实输出设备
                    else if (TryBassInit(1, 48000, "真实设备1/48000Hz")) { _lastError = null; }
                    else if (TryBassInit(1, 44100, "真实设备1/44100Hz")) { _lastError = null; }
                    // 末位保底：no-sound 设备。BASS 能跑通全流程但不发声，必须明确标记告知用户。
                    else
                    {
                        // 走到保底前，先把 BASS 看到的设备全部枚举出来，
                        // 让用户能从 diag.log 看到"为什么连真实设备都没打开"。
                        EnumerateDiagnostics();
                        if (TryBassInit(0, 44100, "无输出设备(保底，不发声)"))
                        {
                            _lastError = null;
                            _usingNoSoundDevice = true;
                        }
                    }
                }
            }

            Diagnostics.Write($"BassRuntime.DoInitialize 结束，返回 {_initialized}");
            return new BassInitResult(_initialized, _usingExclusive, _usingNoSoundDevice, OutputDescription, _lastError);
        }
        catch (DllNotFoundException ex)
        {
            // 首次调用 Bass.* 即触发：原生 bass.dll 未放到 exe 目录。
            _lastError = "缺少原生 BASS 动态库（bass.dll）。请在本机以 PowerShell 运行仓库内 tools/fetch-bass.ps1，" +
                         "自动下载 x64 原生库到 exe 目录后重新启动。原始错误：" + ex.Message;
            Console.Error.WriteLine($"[BassRuntime] {_lastError}");
            return new BassInitResult(false, false, false, null, _lastError);
        }
        catch (Exception ex)
        {
            // 任何其它异常：可能是音频服务没启动 / 默认设备被禁用 / 32-64 位混用等。
            _lastError = "BASS 初始化抛出异常：" + ex.GetType().Name + " - " + ex.Message +
                         "。常见原因：Windows Audio 服务未启动 / 默认输出设备被禁用 / 缺原生 dll。";
            Console.Error.WriteLine($"[BassRuntime] {_lastError}");
            return new BassInitResult(false, false, false, null, _lastError);
        }
    }

    /// <summary>
    /// 与 BassAudioEngine.GetBassErrorText 一致的错码语义；这里独立实现一份，避免循环依赖。
    /// </summary>
    private static string ResolveBassErrorText(int code) => code switch
    {
        0 => "成功",
        1 => "内存不足",
        2 => "文件无法打开（路径不存在 / 被占用 / 无权限；中文等非 ASCII 路径需以 Unicode 传入）",
        3 => "设备驱动错误",
        4 => "音频缓冲区丢失",
        5 => "无效句柄",
        6 => "不支持的采样格式",
        7 => "无效位置",
        8 => "BASS 尚未初始化",
        9 => "设备无法启动",
        14 => "已经初始化过",
        18 => "没有可用的声道",
        19 => "非法类型",
        20 => "非法参数",
        41 => "文件格式不被支持（不是有效的音频文件）",
        44 => "缺少对应的解码器（如 FLAC/APE 需加载 bassflac/bassape 等插件）",
        -1 => "未知错误",
        _ => "未知错误码：" + code
    };

    /// <summary>
    /// 尝试用指定设备/采样率初始化 BASS，成功则记录输出描述。
    /// </summary>
    private bool TryBassInit(int device, int freq, string description)
    {
        Diagnostics.Write($"尝试 BASS_Init({device}, {freq}) [{description}]...");
        try { Bass.BASS_SetConfig(BASSConfig.BASS_CONFIG_UPDATEPERIOD, 20); } catch { }

        _initialized = Bass.BASS_Init(device, freq, BASSInit.BASS_DEVICE_DEFAULT, IntPtr.Zero);
        var code = (int)Bass.BASS_ErrorGetCode();
        Diagnostics.Write($"BASS_Init({device}, {freq}) 返回 {_initialized}, 错误码 {code}");

        if (!_initialized) return false;

        OutputDescription = description;
        Console.WriteLine($"[BassRuntime] BASS_Init 成功：{description}");
        Diagnostics.Write($"BASS_Init 成功：{description}");
        return true;
    }

    /// <summary>
    /// 把当前 BASS 看到的所有输出设备枚举到诊断日志，用于诊断"为什么连默认设备都 Init 失败"。
    /// </summary>
    private static void EnumerateDiagnostics()
    {
        try
        {
            var n = Bass.BASS_GetDeviceCount();
            Diagnostics.Write($"BASS_GetDeviceCount = {n}");
            for (int i = 0; i < n; i++)
            {
                var info = Bass.BASS_GetDeviceInfo(i);
                if (info != null)
                    Diagnostics.Write($"  设备[{i}] Name='{info.name}' IsEnabled={info.IsEnabled} IsDefault={info.IsDefault} flags={info.flags}");
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Write($"BASS_GetDeviceInfo 失败: {ex.GetType().Name} - {ex.Message}");
        }
    }

    /// <summary>
    /// 在后台线程调用 BASS_WASAPI_Init 并等待最多 <paramref name="timeoutMs"/> 毫秒。
    /// 超时或失败都返回 false，由调用方回退共享输出。
    /// </summary>
    /// <remarks>
    /// 为什么必须这么做：BASS_WASAPI_Init 在设备被其它程序独占 / 虚拟声卡不支持独占时，
    /// 可能直接挂住不返回。同步调用会把整个启动链路卡死（表现为"双击没反应、界面不出来"），
    /// 因此放到后台线程 + Join 超时，超时后放弃独占（后台线程标记为 background，随进程退出回收）。
    /// </remarks>
    private bool TryInitWasapiExclusive(int timeoutMs)
    {
        var flags = BassWasapiInit.Exclusive | BassWasapiInit.Stream | BassWasapiInit.Autoformat;
        bool? ok = null;

        var worker = new Thread(() =>
        {
            try { ok = BassWasapi.Init(-1, 0, 0, flags, 0f, 0f); }
            catch (Exception ex)
            {
                ok = false;
                Diagnostics.Write($"BASS_WASAPI_Init 线程内异常: {ex.GetType().Name} - {ex.Message}");
            }
        })
        { IsBackground = true, Name = "BassWasapiInit" };

        try
        {
            worker.Start();
            if (worker.Join(timeoutMs))
            {
                Diagnostics.Write($"BASS_WASAPI_Init 在超时前返回: {ok}");
                return ok == true;
            }

            Diagnostics.Write($"BASS_WASAPI_Init 超时（{timeoutMs}ms 未返回），放弃独占");
            return false;
        }
        catch (Exception ex)
        {
            Diagnostics.Write($"BASS_WASAPI_Init 等待异常: {ex.GetType().Name} - {ex.Message}");
            return false;
        }
    }

    private static void LoadPlugins()
    {
        // MP3 由 bass.dll 原生支持，无需额外插件。以下插件用于 FLAC/Opus/APE/ALAC 等格式。
        foreach (var name in new[] { "bassflac", "bassopus", "bassape", "bassalac" })
        {
            // 缺少对应原生 dll 时返回 0，忽略即可（用户把 bass*.dll 放到 exe 目录即可启用）。
            Diagnostics.Write($"BASS_PluginLoad({name}.dll) 调用前");
            var handle = Bass.BASS_PluginLoad(name + ".dll");
            Console.WriteLine(handle != 0
                ? $"[BassRuntime] 已加载解码插件：{name}"
                : $"[BassRuntime] 解码插件未加载（可忽略，除非需要该格式）：{name}");
            Diagnostics.Write($"BASS_PluginLoad({name}.dll) 返回 handle={handle}");
        }
    }

    public void Dispose()
    {
        if (!_initialized) return;

        try
        {
            if (_usingExclusive)
            {
                BassWasapi.Free();
                try { Bass.BASS_Free(); }
                catch { }
            }
            else
            {
                Bass.BASS_Free();
            }
        }
        catch { }

        _initialized = false;
        _usingExclusive = false;
    }
}
