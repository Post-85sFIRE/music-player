using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Un4seen.Bass;
using MusicPlayer.Core.Interfaces;

namespace MusicPlayer.Audio;

/// <summary>
/// 基于 BASS 的音频引擎实现。浮点处理链、WASAPI 输出。
/// 进度回传走 ~10Hz 定时器，避免 BASS 回调风暴。
/// </summary>
public class BassAudioEngine : IAudioEngine
{
    private readonly BassRuntime _runtime;
    private int _stream;
    private int _nextStream;            // gapless：预加载的下一首流句柄
    private Action? _onNextAdvanced;    // gapless：引擎内无缝切换后回调上层
    private readonly System.Timers.Timer _posTimer = new(100);
    private bool _playing;
    private bool _engineInitialized;    // 保证 Initialize 只执行一次，避免重复订阅 timer
    private bool _timerSubscribed;      // 防止重复订阅 Position 定时器
    private bool _exclusive;            // 当前是否处于 WASAPI 独占模式（决定流/播放的路由方式）

    private SYNCPROC? _endSync;

    /// <summary>最近一次 Load 失败的详细原因（含 BASS 错误码中文说明）。供上层用于界面提示。</summary>
    public string? LastError { get; private set; }

    /// <summary>
    /// 探测 BASS 内部是否真的初始化好。返回 true 表示可用；false 时 LastError 已写入具体原因。
    /// 用来兜底异常路径：在 Load / Play 之前确保 BASS 已 Init，否则任何 BASS_* 调用都会回错误码 8。
    /// </summary>
    private bool EnsureBassReady()
    {
        // Bass.BASS_GetVersion()：未初始化时返回 0。
        try
        {
            if (Bass.BASS_GetVersion() != 0) return true;
        }
        catch (DllNotFoundException ex)
        {
            LastError = "缺少原生 BASS 动态库（bass.dll）。请在本机运行 tools/fetch-bass.ps1 后重新启动。原始错误：" + ex.Message;
            Console.Error.WriteLine($"[BassAudioEngine] EnsureBassReady: {LastError}");
            return false;
        }
        catch (Exception ex)
        {
            // 任何其它异常都按"未初始化"处理，交由 Initialize 重试。
            Console.Error.WriteLine($"[BassAudioEngine] EnsureBassReady GetVersion 异常: {ex.Message}");
        }

        // 走到这里说明 BASS 内部实际未初始化。强制重试一次 Initialize。
        // 取消旧的"已初始化"假象标志，让 Initialize 真正去走 BASS_Init。
        _engineInitialized = false;
        if (!Initialize())
        {
            LastError = "BASS 尚未初始化（请先调用 Initialize）— 自动重试初始化失败：" + (_runtime.LastError ?? "原因未知");
            Console.Error.WriteLine($"[BassAudioEngine] EnsureBassReady: 自动重试 Initialize 失败: {_runtime.LastError}");
            return false;
        }
        return true;
    }

    public event EventHandler? PlaybackEnded;
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;
    public event EventHandler<StateChangedEventArgs>? StateChanged;
    /// <summary>独占模式下播放启动失败（BASS_WASAPI_ChannelPlay 返回 false）时触发，供上层自动退回共享模式。</summary>
    public event EventHandler? ExclusivePlaybackFailed;

    public BassAudioEngine(BassRuntime runtime) => _runtime = runtime;

    public bool Initialize()
    {
        if (_engineInitialized) return true;

        // 用运行时的实际模式（ExclusiveMode）：独占失败时 BassRuntime 内部已回退共享。
        if (!_runtime.Initialize(_runtime.ExclusiveMode).Success) return false;

        FinishInit();
        return true;
    }

    /// <summary>
    /// 运行时重新初始化（切换 WASAPI 独占开关时调用，无需重启）。
    /// 先释放当前流，再按期望的独占开关让运行时完整重跑初始化+回退链，最后恢复定时器与模式标志。
    /// 返回 true 表示音频引擎重新就绪（无论最终是独占还是共享）。
    /// </summary>
    public bool Reinitialize(bool exclusive)
    {
        FreeStream();
        FreeNext();
        _engineInitialized = false;
        _exclusive = false;
        _playing = false;

        // 设置期望模式并让运行时重跑：独占失败会自动回退共享/默认设备。
        _runtime.ExclusiveMode = exclusive;
        if (!_runtime.Reinitialize(_runtime.ExclusiveMode).Success) return false;

        FinishInit();
        return true;
    }

    /// <summary>当前是否处于 WASAPI 独占模式（供 UI 提示与诊断）。</summary>
    public bool IsExclusive => _runtime.IsExclusive;

    /// <summary>当前是否退到了无输出保底设备（BASS 能跑通但不发声）。</summary>
    public bool UsingNoSoundDevice => _runtime.UsingNoSoundDevice;

    /// <summary>初始化成功后的公共收尾：订阅 Position 定时器（仅一次）、同步独占模式标志。</summary>
    private void FinishInit()
    {
        if (!_timerSubscribed)
        {
            _posTimer.AutoReset = true;
            _posTimer.Elapsed += (_, _) => ReportPosition();
            _timerSubscribed = true;
        }
        _exclusive = _runtime.IsExclusive;
        _engineInitialized = true;
    }

    public bool Load(string filePath)
    {
        FreeStream();
        FreeNext();
        LastError = null;

        if (string.IsNullOrWhiteSpace(filePath))
        {
            LastError = "文件路径为空，无法加载。";
            Console.Error.WriteLine("[BassAudioEngine] 加载失败：传入的文件路径为空。");
            return false;
        }

        if (!File.Exists(filePath))
        {
            LastError = $"文件不存在：{filePath}";
            Console.Error.WriteLine($"[BassAudioEngine] 加载失败：文件不存在: {filePath}");
            return false;
        }

        // 防御性兜底：若 BASS 内部状态其实并未初始化（极端情况下 _engineInitialized 被错误置 true，
        // 或某次异常路径绕过了真正的 BASS_Init），这里用 Bass.BASS_GetVersion() 探测一次：
        // 未初始化时该 API 返回 0；返回非零则 BASS 已就绪。
        if (!EnsureBassReady())
        {
            // EnsureBassReady 内部已写 LastError，直接返回 false 走弹窗提示。
            return false;
        }

        _stream = CreateStream(filePath);
        if (_stream == 0) return false;

        // 播放结束：若已预加载下一首则引擎内无缝切换；否则通知上层走播放列表逻辑。
        _endSync = (handle, channel, data, user) =>
        {
            if (_nextStream != 0)
            {
                var advanced = _nextStream;
                _nextStream = 0;
                KillChannel(_stream);
                _stream = advanced;
                Bass.BASS_ChannelSetSync(_stream, BASSSync.BASS_SYNC_END, 0, _endSync, IntPtr.Zero);
                StartChannel(_stream, false);
                _onNextAdvanced?.Invoke();
                return;
            }
            _playing = false;
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        };
        Bass.BASS_ChannelSetSync(_stream, BASSSync.BASS_SYNC_END, 0, _endSync, IntPtr.Zero);

        return true;
    }

    /// <summary>
    /// 按字符串路径打开文件流；返回流句柄，0 表示失败（LastError 已记录）。
    /// </summary>
    /// <remarks>
    /// BASS_StreamCreateFile 的重载很容易踩坑：
    /// - <c>string</c> 重载加 <see cref="BASSFlag.BASS_UNICODE"/> 才能正确走 Unicode 路径；
    ///   某些运行时下 .NET 封送会有兼容问题，回错误码 20（BASS_ERROR_ILLPARAM）。
    /// - <c>IntPtr memory</c> 重载参数 offset=length=0 表示"用 STREAMFILEPROC 回调"，直接传路径指针也会回 20。
    /// 这里采用两层策略：先 string 重载；若失败，则把整个文件读入内存后用 memory 重载加载。
    /// 内存加载能彻底绕过路径编码问题，对大文件会多占一点内存，但兼容性最好。
    /// </remarks>
    private int CreateStream(string filePath)
    {
        Diagnostics.Write($"CreateStream 开始: filePath=[{filePath}] (exclusive={_exclusive})");

        var flags = BASSFlag.BASS_SAMPLE_FLOAT | BASSFlag.BASS_STREAM_PRESCAN;
        // 独占模式下 WASAPI 直接从「解码流」拉数据输出，流必须是解码流（无需 BASS 输出设备）。
        if (_exclusive) flags |= BASSFlag.BASS_STREAM_DECODE;

        // 1. string 重载 + BASS_UNICODE（常规路径，让 BASS 自己处理 Unicode）。
        Diagnostics.Write("CreateStream 尝试 string 重载 + BASS_UNICODE...");
        var stream = Bass.BASS_StreamCreateFile(filePath, 0, 0, flags | BASSFlag.BASS_UNICODE);
        var firstCode = (int)Bass.BASS_ErrorGetCode();
        Diagnostics.Write($"CreateStream string 重载返回 {stream}, 错误码 {firstCode}");
        if (stream != 0) return stream;

        // 2. 内存加载 fallback：完全绕过路径编码，兼容所有中文/特殊字符路径。
        Diagnostics.Write("CreateStream string 重载失败，尝试内存加载 fallback...");
        try
        {
            var fileBytes = File.ReadAllBytes(filePath);
            var memPtr = Marshal.AllocHGlobal(fileBytes.Length);
            try
            {
                Marshal.Copy(fileBytes, 0, memPtr, fileBytes.Length);
                stream = Bass.BASS_StreamCreateFile(memPtr, 0, fileBytes.Length, flags);
                var secondCode = (int)Bass.BASS_ErrorGetCode();
                Diagnostics.Write($"CreateStream 内存加载返回 {stream}, 错误码 {secondCode}");
            }
            finally
            {
                Marshal.FreeHGlobal(memPtr);
            }

            if (stream != 0) return stream;
        }
        catch (Exception ex)
        {
            Diagnostics.Write($"CreateStream 内存加载异常: {ex.GetType().Name} - {ex.Message}");
        }

        var finalCode = (int)Bass.BASS_ErrorGetCode();
        LastError = $"BASS 加载失败（错误码 {finalCode}）：{GetBassErrorText(finalCode)}";
        Console.Error.WriteLine($"[BassAudioEngine] 加载失败: {filePath} -> 错误码 {finalCode} ({GetBassErrorText(finalCode)})");
        Diagnostics.Write($"CreateStream 最终失败: {LastError}");
        return stream;
    }

    /// <summary>预加载下一首流，供播放结束瞬间无缝切换（gapless）。加载失败则不接管。</summary>
    public void PrepareNext(string filePath, Action onAdvanced)
    {
        FreeNext();
        _onNextAdvanced = onAdvanced;
        _nextStream = CreateStream(filePath);
        if (_nextStream == 0) _onNextAdvanced = null;
    }

    public void Play()
    {
        if (_stream == 0) return;
        var ok = StartChannel(_stream, false);
        if (_exclusive && !ok)
        {
            // 独占模式下 BASS_WASAPI_ChannelPlay 失败：WASAPI 拉不起这条解码流（设备被占/格式不支持等）。
            // 记录原因并通知上层自动退回共享模式（见 ExclusivePlaybackFailed）。
            LastError = "WASAPI 独占播放启动失败（BASS_WASAPI_ChannelPlay 返回 false）。";
            Console.Error.WriteLine($"[BassAudioEngine] {LastError}");
            Diagnostics.Write(LastError);
            _playing = false;
            ExclusivePlaybackFailed?.Invoke(this, EventArgs.Empty);
            return;
        }
        _playing = true;
        _posTimer.Start();
        StateChanged?.Invoke(this, new StateChangedEventArgs { IsPlaying = true });
    }

    /// <summary>
    /// 让一条流开始输出：独占模式交给 WASAPI 拉数据，共享模式走 BASS 标准输出设备。
    /// 返回 ChannelPlay 是否成功（独占模式据此判断是否需回退共享）。
    /// </summary>
    private bool StartChannel(int stream, bool restart)
    {
        if (_exclusive)
        {
            var ok = BassWasapi.ChannelPlay(stream, restart);
            if (!ok) return false;
            // 兜底：确保 WASAPI 设备已进入运行态（部分 BASS 构建下 ChannelPlay 不会自动起设备，
            // 会表现为"初始化成功却不出声"）。设备已在运行则 Start 为幂等 no-op。
            if (!BassWasapi.Start()) return false;
        }
        else
        {
            Bass.BASS_ChannelPlay(stream, restart);
        }
        return true;
    }

    public void Pause()
    {
        if (_stream == 0) return;
        KillChannel(_stream, stopOnly: true);
        _playing = false;
        _posTimer.Stop();
        StateChanged?.Invoke(this, new StateChangedEventArgs { IsPlaying = false });
    }

    public void Stop()
    {
        KillChannel(_stream);
        _stream = 0;
        FreeNext();
        _playing = false;
        _posTimer.Stop();
        StateChanged?.Invoke(this, new StateChangedEventArgs { IsPlaying = false });
    }

    /// <summary>
    /// 停止一条流的输出。stopOnly=true 仅暂停/停止输出、保留流句柄（用于 Pause）；
    /// false 则连流本身一起释放（用于 Stop / 无缝切换旧流）。
    /// </summary>
    private void KillChannel(int stream, bool stopOnly = false)
    {
        if (stream == 0) return;
        try
        {
            if (_exclusive) BassWasapi.ChannelStop(stream);
            else Bass.BASS_ChannelStop(stream);
        }
        catch { /* 设备可能已释放，忽略 */ }

        if (!stopOnly)
        {
            try { Bass.BASS_StreamFree(stream); } catch { }
        }
    }

    public void Seek(TimeSpan position)
    {
        if (_stream == 0) return;
        var bytes = Bass.BASS_ChannelSeconds2Bytes(_stream, position.TotalSeconds);
        Bass.BASS_ChannelSetPosition(_stream, bytes, BASSMode.BASS_POS_BYTE);
    }

    public TimeSpan Position
    {
        get
        {
            if (_stream == 0) return TimeSpan.Zero;
            var bytes = Bass.BASS_ChannelGetPosition(_stream, BASSMode.BASS_POS_BYTE);
            return TimeSpan.FromSeconds(Bass.BASS_ChannelBytes2Seconds(_stream, bytes));
        }
    }

    public TimeSpan Duration
    {
        get
        {
            if (_stream == 0) return TimeSpan.Zero;
            var bytes = Bass.BASS_ChannelGetLength(_stream, BASSMode.BASS_POS_BYTE);
            return TimeSpan.FromSeconds(Bass.BASS_ChannelBytes2Seconds(_stream, bytes));
        }
    }

    public void SetVolume(float linear)
    {
        if (_stream == 0) return;
        var v = Math.Clamp(linear, 0f, 1f);
        if (_exclusive)
        {
            // 独占模式下没有「每流输出增益」属性，改用 WASAPI 流音量（部分设备不支持则忽略）。
            try { BassWasapi.SetVolume(BassWasapiVolume.Stream, v); }
            catch { /* 设备不支持 SetVolume，忽略，不影响播放 */ }
        }
        else
        {
            Bass.BASS_ChannelSetAttribute(_stream, BASSAttribute.BASS_ATTRIB_VOL, v);
        }
    }

    public bool IsPlaying => _playing;

    private void ReportPosition()
    {
        if (_stream == 0) return;
        PositionChanged?.Invoke(this, new PositionChangedEventArgs { Position = Position, Duration = Duration });
    }

    private void FreeStream()
    {
        KillChannel(_stream);
        _stream = 0;
        _posTimer.Stop();
    }

    private void FreeNext()
    {
        if (_nextStream != 0)
        {
            Bass.BASS_StreamFree(_nextStream);
            _nextStream = 0;
        }
        _onNextAdvanced = null;
    }

    /// <summary>将 BASS 错误码翻译成可读中文说明，便于日志与界面提示定位。</summary>
    /// <remarks>BASS 错误码在不同版本中稳定，这里用整型常量匹配，避免依赖具体枚举成员名。</remarks>
    private static string GetBassErrorText(int code) => code switch
    {
        0 => "成功",
        1 => "内存不足",
        2 => "文件无法打开（路径不存在 / 被占用 / 无权限；注意中文等非 ASCII 路径需以 Unicode 传入）",
        3 => "设备驱动错误",
        4 => "音频缓冲区丢失",
        5 => "无效句柄",
        6 => "不支持的采样格式",
        7 => "无效位置",
        8 => "BASS 尚未初始化（请先调用 Initialize）",
        9 => "设备无法启动",
        14 => "已经初始化",
        18 => "没有可用的声道",
        19 => "非法类型",
        20 => "非法参数",
        21 => "3D 不可用",
        22 => "EAX 不可用",
        27 => "不是文件流",
        37 => "功能不可用",
        38 => "解码失败",
        41 => "文件格式不被支持（不是有效的音频文件）",
        44 => "缺少对应的解码器（如 FLAC/APE 需加载 bassflac/bassape 等插件）",
        45 => "已结束",
        46 => "设备忙",
        -1 => "未知错误",
        _ => "未知错误码：" + code
    };

    public void Dispose()
    {
        _posTimer.Dispose();
        FreeStream();
        FreeNext();
    }
}
