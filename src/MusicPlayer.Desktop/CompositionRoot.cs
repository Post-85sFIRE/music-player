using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MusicPlayer.Audio;
using MusicPlayer.Core.Interfaces;
using MusicPlayer.Data;
using MusicPlayer.Data.Db;
using MusicPlayer.Data.Repositories;
using MusicPlayer.Desktop.ViewModels;
using MusicPlayer.Services;
using Un4seen.Bass;

namespace MusicPlayer.Desktop;

/// <summary>
/// 组合根：唯一装配所有依赖的地方。
/// </summary>
public static class CompositionRoot
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();

        // 真便携：数据目录落在 exe 同目录（AppContext.BaseDirectory），换电脑/U盘随身；
        // 设置、曲库、播放列表、云源配置全部存于该目录下的 library.db。
        var appDir = AppContext.BaseDirectory;
        Directory.CreateDirectory(appDir);

        // 每次启动重新记一轮诊断日志（BASS 初始化步骤会逐步落盘，便于定位卡点）。
        Diagnostics.Reset();
        Diagnostics.Write("CompositionRoot.Build 开始");

        var dbPath = Path.Combine(appDir, "library.db");
        var coverCache = Path.Combine(appDir, "Covers");

        var factory = new AppDbFactory(dbPath);
        DatabaseInitializer.EnsureCreated(factory);

        services.AddSingleton(factory);
        services.AddSingleton<TrackRepository>();
        services.AddSingleton<ITrackRepository>(sp => sp.GetRequiredService<TrackRepository>());
        services.AddSingleton<PlaylistRepository>();
        services.AddSingleton<IPlaylistRepository>(sp => sp.GetRequiredService<PlaylistRepository>());
        services.AddSingleton<SettingsRepository>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<SettingsService>());
        services.AddSingleton<INotificationService, MessageBoxNotificationService>();

        services.AddSingleton<LibraryScanner>(sp =>
            new LibraryScanner(sp.GetRequiredService<TrackRepository>(),
                sp.GetRequiredService<PlaylistRepository>(), coverCache, null));
        services.AddSingleton<ILibraryScanner>(sp => sp.GetRequiredService<LibraryScanner>());
        services.AddSingleton<SpaceStatisticsService>();
        services.AddSingleton<CloudSourceRegistry>();
        // 缓存根 = 下载/缓存目录（设置项，缺省用 MyMusic/MusicPlayerCache）；解析时读取已加载的设置。
        services.AddSingleton<IDownloadCacheService>(sp =>
        {
            var svc = sp.GetRequiredService<SettingsService>();
            var root = string.IsNullOrWhiteSpace(svc.Settings.DownloadPath)
                ? SettingsService.DefaultDownloadPath()
                : svc.Settings.DownloadPath;
            return new DownloadCacheService(root);
        });
        services.AddSingleton<CloudViewModel>();

        services.AddSingleton<BassRuntime>();
        services.AddSingleton<BassAudioEngine>();
        services.AddSingleton<IAudioEngine>(sp => sp.GetRequiredService<BassAudioEngine>());

        services.AddSingleton<IPlaylistManager, PlaylistManager>();
        services.AddSingleton<LyricsService>();
        services.AddSingleton<PlaybackManager>();

        services.AddSingleton<PlaybackViewModel>();
        services.AddSingleton<PlaylistViewModel>();
        services.AddSingleton<LibraryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();

        var provider = services.BuildServiceProvider();

        // 音频引擎可用性预检：缺原生 bass.dll 时记录原因，由 App 启动时代出中文提示，
        // 避免首次播放才崩溃。（此处可安全引用 MusicPlayer.Audio；App.xaml.cs 经 WPF 临时工程
        // 编译，不带 Audio 引用，故预检逻辑放在组合根。）
        // 预检与实际播放使用同一模式：独占模式需在初始化时即决定（同一设备不可重复 Init）。
        var settingsSvc = provider.GetRequiredService<SettingsService>();
        settingsSvc.Load();
        var rt = provider.GetRequiredService<BassRuntime>();
        rt.ExclusiveMode = settingsSvc.Settings.ExclusiveWasapi;

        // M2：把已保存的云源配置实例化为 provider 并注册到 CloudSourceRegistry。
        var registry = provider.GetRequiredService<CloudSourceRegistry>();
        foreach (var cfg in settingsSvc.Settings.CloudSources)
        {
            ICloudSourceProvider p = new WebDavCloudProvider(cfg);
            registry.Register(p);
        }

        // 注意：这里**不初始化音频引擎**。BASS 初始化（尤其 WASAPI 独占）在设备被占用时可能挂起，
        // 放在窗口显示前会把整个启动链路卡死，表现为"双击没反应 / 界面不出来"。
        // 改为由 App 在主窗口 Show() 之后调用 InitializeAudio()。
        Diagnostics.Write("CompositionRoot.Build 结束（音频初始化推迟到窗口显示后）");

        return provider;
    }

    /// <summary>
    /// 初始化音频引擎并做启动期诊断。
    /// **必须在主窗口 Show() 之后调用**（通常在后台 STA 线程），
    /// 这样即使 BASS 初始化缓慢/挂起，界面也已经呈现在用户面前。
    /// </summary>
    public static void InitializeAudio(ServiceProvider provider)
    {
        var rt = provider.GetRequiredService<BassRuntime>();

        try
        {
            // 直接初始化 IAudioEngine（也就是 BassAudioEngine + BassRuntime 这对实例），
            // 确保启动时音频引擎与运行时状态完全一致，避免运行时与引擎实例错位导致 BASS_ERROR_INIT。
            var engine = provider.GetRequiredService<IAudioEngine>();
            if (!engine.Initialize())
            {
                AudioInitError = rt.LastError ?? "音频引擎初始化失败，原因未知。";
            }
        }
        catch (DllNotFoundException ex)
        {
            AudioInitError = "未找到原生 BASS 动态库 bass.dll。请在本机以 PowerShell 运行 tools/fetch-bass.ps1 后重启。\n" + ex.Message;
        }
        catch (Exception ex)
        {
            // 任何其它异常都不允许冒到 App.OnStartup（否则 WPF 会静默吞异常，连主窗口都不显示）。
            AudioInitError = "音频引擎初始化时抛出异常：" + ex.GetType().Name + " - " + ex.Message;
        }

        // 即便 engine.Initialize() 返回 true，也独立再问一次 BASS 的真实状态。
        // 用于捕获"BASS 自以为 Init 成功、但任何 BASS_* 调用都回错误码 8"的诡异边界。
        // 注意：这里的 P/Invoke 必须放在组合根（有 Audio/Un4seen.Bass 引用且能完整 try/catch），
        // 绝不能放进 App.xaml.cs——那会经 wpftmp 临时工程编译，且异常会直接崩掉 OnStartup。
        ProbeBassRuntime();

        // 诊断落盘：沙箱/无显示器环境下 Console 输出常被吞，写文件才能看到真相；
        // 用户报障时也可直接把这份日志发来定位。
        AudioOutputDescription = rt.OutputDescription;
        UsingNoSoundDevice = rt.UsingNoSoundDevice;
        WriteDiagnostics();
    }

    /// <summary>实际生效的音频输出模式（如"默认设备/44100Hz"），供 UI 展示。</summary>
    public static string? AudioOutputDescription { get; private set; }

    /// <summary>是否退到了 no-sound 设备（BASS 能跑通但不发声），UI 必须明确提示。</summary>
    public static bool UsingNoSoundDevice { get; private set; }

    /// <summary>
    /// 最近一次构建时音频初始化检查的结论；为空表示正常。供 App 启动展示提示。
    /// </summary>
    public static string? AudioInitError { get; private set; }

    /// <summary>
    /// BASS 探测失败的具体原因；为空表示 BASS 真实就绪（配合 <see cref="BassVersionText"/> 使用）。
    /// </summary>
    public static string? BassProbeError { get; private set; }

    /// <summary>BASS 探测成功时记录的版本号文本（如 "2.4.17.0"）；探测失败时为 null。</summary>
    public static string? BassVersionText { get; private set; }

    /// <summary>
    /// 独立探测一次 BASS 内部是否真的初始化好，结果写入 <see cref="BassProbeError"/> 或 <see cref="BassVersionText"/>。
    /// 吞掉所有异常：探测只是诊断手段，绝不能影响主窗口显示。
    /// </summary>
    private static void ProbeBassRuntime()
    {
        try
        {
            var v = Bass.BASS_GetVersion();
            if (v != 0)
            {
                BassVersionText = $"{(v >> 24) & 0xFF}.{(v >> 16) & 0xFF}.{(v >> 8) & 0xFF}.{v & 0xFF}";
                Console.WriteLine($"[CompositionRoot] BASS v{BassVersionText} 已就绪。");
                return;
            }

            BassProbeError =
                "BASS_GetVersion() 返回 0，说明原生 BASS 实际并未初始化。" +
                "常见原因：bass.dll 与 Bass.Net.dll 版本不匹配 / Windows Audio 服务未启动 / 默认输出设备被禁用。";
        }
        catch (DllNotFoundException ex)
        {
            BassProbeError = "未找到原生 bass.dll。\n请在本机运行 tools/fetch-bass.ps1 后重新启动。\n\n原始错误：" + ex.Message;
        }
        catch (BadImageFormatException ex)
        {
            BassProbeError = "原生 bass.dll 与程序位数不匹配（本程序为 x64，必须放 x64 版 bass.dll）。\n\n原始错误：" + ex.Message;
        }
        catch (Exception ex)
        {
            BassProbeError = "BASS 探测抛出异常：" + ex.GetType().Name + " - " + ex.Message;
        }

        Console.Error.WriteLine($"[CompositionRoot] BASS 探测失败: {BassProbeError}");
        Diagnostics.Write("BASS 探测失败: " + BassProbeError);
    }

    /// <summary>
    /// 把启动期音频诊断结论追加到 diag.log，便于无显示器/报障场景定位。失败不影响启动。
    /// </summary>
    private static void WriteDiagnostics()
    {
        try
        {
            Console.Out.Flush();
            Diagnostics.Write($"BASS 版本     : {BassVersionText ?? "(探测失败)"}");
            Diagnostics.Write($"输出模式      : {AudioOutputDescription ?? "(未初始化)"}");
            Diagnostics.Write($"无输出设备    : {UsingNoSoundDevice}");
            Diagnostics.Write($"初始化错误    : {AudioInitError ?? "(无)"}");
            Diagnostics.Write($"BASS 探测错误 : {BassProbeError ?? "(无)"}");
            try
            {
                Diagnostics.Write($"BASS_GetVersion 直读 : 0x{Bass.BASS_GetVersion():X8}");
                Diagnostics.Write($"BASS_ErrorGetCode    : {(int)Bass.BASS_ErrorGetCode()}");
            }
            catch (Exception ex)
            {
                Diagnostics.Write($"BASS 直读异常 : {ex.GetType().Name} - {ex.Message}");
            }
            Diagnostics.Write("CompositionRoot.Build 结束");
            Console.Out.Flush();
        }
        catch
        {
            // 诊断落盘失败无所谓，绝不能影响启动。
        }
    }
}
