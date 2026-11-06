using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using static OverlayProbe.NativeMethods;

namespace OverlayProbe;

/// Throwaway tool for PLAN.MD phase 0 bis. No arguments: the hotkey window to try in real games.
/// --audio-check: does the process-excluding loopback keep our own sound out of the capture?
/// --tone: plays a control tone. Start it from outside, during --audio-check: a child would count as excluded.
/// --selftest [--hardware]: shows the notification 3 times and logs its CPU cost, then exits.
public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            new ProbeLog().Write($"CRASH: {args.Exception}");
            args.Handled = true;
            Shutdown(1);
        };

        if (e.Args.Contains("--tone"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            await Task.Run(() => Sounds.Play(Sounds.OtherAppWav()));
            Shutdown();
        }
        else if (e.Args.Contains("--audio-check"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var log = new ProbeLog();
            try
            {
                await AudioCheck.RunAsync(log);
            }
            catch (Exception ex)
            {
                log.Write($"audio check FAILED: {ex}");
            }

            Shutdown();
        }
        else if (e.Args.Contains("--selftest"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            await SelfTestAsync(softwareOnly: !e.Args.Contains("--hardware"));
            Shutdown();
        }
        else
        {
            MainWindow = new MainWindow();
            MainWindow.Show();
        }
    }

    private static async Task SelfTestAsync(bool softwareOnly)
    {
        var log = new ProbeLog();
        var window = new NotificationWindow(softwareOnly);
        var process = Process.GetCurrentProcess();
        var foreground = GetForegroundWindow();
        var monitor = MonitorFromWindow(foreground, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfo(monitor, ref info);
        GetDpiForMonitor(monitor, 0, out var dpi, out _);

        var finished = new TaskCompletionSource();
        window.Finished += () => finished.TrySetResult();

        // The first pass includes WPF's first render: only the later ones count.
        for (var pass = 1; pass <= 3; pass++)
        {
            finished = new TaskCompletionSource();
            process.Refresh();
            var before = process.TotalProcessorTime;
            window.Present(info.Monitor, dpi, clickThrough: true);
            await finished.Task;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            process.Refresh();
            var cost = process.TotalProcessorTime - before;
            log.Write($"selftest {(softwareOnly ? "software" : "hardware")} pass {pass}: {cost.TotalMilliseconds:F0} ms of CPU over 2.92 s "
                + $"({cost.TotalMilliseconds / 2920 * 100:F1} % of one core), RAM {process.WorkingSet64 / 1048576} MB, "
                + $"foreground kept: {GetForegroundWindow() == foreground}");
            await Task.Delay(500);
        }

        process.Refresh();
        var idleStart = process.TotalProcessorTime;
        await Task.Delay(10000);
        process.Refresh();
        log.Write($"selftest idle: {(process.TotalProcessorTime - idleStart).TotalMilliseconds:F0} ms of CPU over 10 s, RAM {process.WorkingSet64 / 1048576} MB");
        window.Close();
    }
}
