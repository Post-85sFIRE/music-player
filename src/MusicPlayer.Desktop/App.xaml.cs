using System;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using MusicPlayer.Desktop.ViewModels;
using MusicPlayer.Services;

namespace MusicPlayer.Desktop;

public partial class App : System.Windows.Application
{
    public static ServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 关键顺序：先让主窗口显示出来，再做音频诊断提示。
        // 任何诊断/弹窗异常都不允许阻止窗口出现（WPF 会静默吞掉 OnStartup 异常，表现为"双击没反应"）。
        try
        {
            Services = CompositionRoot.Build();
            Services.GetRequiredService<SettingsService>().Load();
        }
        catch (Exception ex)
        {
            // 组合根都失败了（DI/数据库等致命问题），此时无法构造主窗口，只能弹错后退出。
            System.Windows.MessageBox.Show(
                "程序启动失败，无法初始化核心组件。\n\n" + ex.GetType().Name + " - " + ex.Message +
                "\n\n" + ex.StackTrace,
                "音乐播放器 - 启动失败",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown();
            return;
        }

        Views.MainWindow? main = null;
        try
        {
            main = new Views.MainWindow
            {
                DataContext = Services.GetRequiredService<MainViewModel>()
            };
            MainWindow = main;
            main.Show();
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(
                "主窗口创建失败。\n\n" + ex.GetType().Name + " - " + ex.Message + "\n\n" + ex.StackTrace,
                "音乐播放器 - 界面初始化失败",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            Shutdown();
            return;
        }

        // 窗口已经显示，下面这些提示弹窗不会影响启动。
        StartAudioOnBackgroundThread();

        // 启动即后台自动重连已配置的云盘（凭据已持久化）：构造 CloudViewModel 单例会触发其
        // ReconnectSavedAsync，使"云盘音乐可直接播放"且连接状态在打开云盘窗口时可见。
        // 放在主窗口显示之后，确保 UI 线程已就绪；重连本身是 fire-and-forget，不阻塞启动。
        _ = Services.GetRequiredService<CloudViewModel>();
    }

    /// <summary>
    /// 在主窗口显示之后，用后台 STA 线程初始化音频引擎。
    /// 这样即使 BASS 初始化（尤其 WASAPI 独占抢设备）缓慢或挂起，界面也已经呈现给用户，
    /// 不会再出现"双击没反应 / 界面不出来"的情况。初始化完成后回到 UI 线程显示诊断结论。
    /// </summary>
    private static void StartAudioOnBackgroundThread()
    {
        var thread = new Thread(() =>
        {
            CompositionRoot.InitializeAudio(Services);

            // 回到 UI 线程展示诊断结论（成功则更新标题栏，失败则弹窗）。
            Current.Dispatcher.Invoke(ShowAudioDiagnostics);
        })
        {
            IsBackground = true,
            Name = "AudioInit"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    /// <summary>
    /// 音频引擎诊断提示。只读 CompositionRoot 已经算好的结果（BASS 的 P/Invoke 探测在组合根里做），
    /// 这里不直接触碰 BASS，避免 wpftmp 临时工程引用问题与 P/Invoke 异常冒泡。
    /// </summary>
    private static void ShowAudioDiagnostics()
    {
        try
        {
            // 1) 初始化阶段就失败：给出明确原因与解决办法。
            if (!string.IsNullOrEmpty(CompositionRoot.AudioInitError))
            {
                System.Windows.MessageBox.Show(
                    "音频引擎初始化未完成，当前可能无法出声。\n\n原因：" + CompositionRoot.AudioInitError +
                    "\n\n解决办法：在本机以 PowerShell 运行 tools/fetch-bass.ps1，" +
                    "自动下载 x64 原生 BASS 动态库到 exe 目录，然后重新启动本程序。",
                    "音乐播放器 - 音频初始化提示",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // 2) 初始化"看起来成功"但 BASS 实际未就绪：专门提示，指向服务/版本/设备方向。
            if (!string.IsNullOrEmpty(CompositionRoot.BassProbeError))
            {
                System.Windows.MessageBox.Show(
                    "BASS 引擎状态异常：" + CompositionRoot.BassProbeError +
                    "\n\n请尝试：\n" +
                    "  1) 重新运行 tools/fetch-bass.ps1，确保 exe 目录拿到最新 x64 原生 dll；\n" +
                    "  2) 在 services.msc 确认 Windows Audio / Windows Audio Endpoint Builder 已启动；\n" +
                    "  3) 确认默认输出设备（扬声器/耳机）未被禁用；\n" +
                    "  4) 重启本程序。",
                    "音乐播放器 - BASS 状态异常",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            // 3) 一切正常：把版本号 + 实际输出模式写进标题栏，作为"BASS 已就绪"的可见凭据。
            if (!string.IsNullOrEmpty(CompositionRoot.BassVersionText))
            {
                Console.WriteLine($"[App] BASS v{CompositionRoot.BassVersionText} 已就绪。");
                if (Current.MainWindow is Views.MainWindow w)
                {
                    w.Title = $"音乐播放器  -  BASS v{CompositionRoot.BassVersionText}  ·  {CompositionRoot.AudioOutputDescription}";
                }
            }

            // 4) 退到 no-sound 设备：BASS 全流程能跑通但不会发声，必须明确告知，
            //    否则用户会以为程序坏了（这是最难自查的一类问题）。
            if (CompositionRoot.UsingNoSoundDevice)
            {
                System.Windows.MessageBox.Show(
                    "未能打开任何真实音频输出设备，程序已退到「无输出设备」模式。\n" +
                    "此时播放列表、进度等都能跑，但**不会发出声音**。\n\n" +
                    "请检查：\n" +
                    "  1) 扬声器/耳机已连接，且在系统音量设置里未被禁用；\n" +
                    "  2) 在 services.msc 中确认 Windows Audio 服务已启动；\n" +
                    "  3) 若有其它程序独占音频设备（设置里开启了 WASAPI 独占），先关闭它或关闭独占；\n" +
                    "  4) 重启本程序。",
                    "音乐播放器 - 无音频输出设备",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            // 诊断弹窗本身出错绝不能影响已经显示的主窗口。
            Console.Error.WriteLine($"[App] 音频诊断提示失败: {ex.Message}");
        }
    }
}
