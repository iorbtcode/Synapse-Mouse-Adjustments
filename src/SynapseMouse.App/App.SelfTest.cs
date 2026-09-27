using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using SynapseMouse.App.Infrastructure;
using SynapseMouse.App.Services;

namespace SynapseMouse.App;

/// <summary>
/// --self-test: launches the real UI with temporary settings, visits every page, exercises Master Enable,
/// config switching and input-engine reconfiguration, and fails on any exception or WPF binding error.
/// Used by CI on a Windows runner to verify the app at runtime, not just that it compiles.
/// </summary>
internal partial class App
{
    private readonly List<string> _selfTestFailures = new();
    private readonly List<string> _selfTestLog = new();
    private bool _selfTestFinished;

    private void StartSelfTest()
    {
        var listener = new BindingErrorListener(_selfTestFailures);
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;

        // Hard stop in case something hangs.
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(120) };
        watchdog.Tick += (_, _) =>
        {
            _selfTestFailures.Add("Self-test timed out.");
            FinishSelfTest();
        };
        watchdog.Start();

        Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await RunSelfTestAsync();
            }
            catch (Exception ex)
            {
                _selfTestFailures.Add("Self-test exception: " + ex);
            }

            FinishSelfTest();
        }, DispatcherPriority.ApplicationIdle);
    }

    private async Task RunSelfTestAsync()
    {
        var controller = _controller!;
        var main = _mainViewModel!;
        Note($"Started. Engine running: {controller.Engine.IsRunning}. Configs: {controller.Configs.Configs.Count}.");

        ShowMainWindow(null);
        await Settle();
        await VisitAllPages("initial");

        // Switch through every config (each applies different engine + Windows settings).
        foreach (var config in controller.Configs.Configs.ToList())
        {
            controller.SwitchConfig(config.Id, SwitchReason.Manual);
            await Settle();
            Check(controller.ActiveConfig.Id == config.Id, $"Switched to '{config.Name}'");
        }

        // Create and delete a config while the Configs and Dashboard pages are live.
        main.Navigate("configs");
        await Settle();
        var created = controller.Configs.Create("Self-test config", Core.Config.ConfigPresets.MinecraftPvp);
        controller.SwitchConfig(created.Id, SwitchReason.Manual);
        await Settle();
        main.Navigate("dashboard");
        await Settle();
        Check(controller.Configs.Delete(created.Id), "Created and deleted a config");
        await Settle();
        Check(controller.Configs.Find(controller.ActiveConfig.Id) is not null, "Active config valid after delete");

        // Exercise features through the view models, exactly like the UI does.
        var dashboard = main.Dashboard;
        dashboard.DebounceEnabled = true;
        dashboard.DebounceMs = 6;
        dashboard.DoubleClickEnabled = true;
        dashboard.DoubleClickInterval = 45;
        dashboard.DpiStagesEnabled = true;
        dashboard.SelectStageCommand.Execute(0);
        await Settle();
        Check(controller.ActiveConfig.Debounce.TimeMs == 6, "Debounce edit applied");
        Check(controller.CurrentSettings.NeedsMouseHook, "Engine settings need the mouse hook");
        await WaitFor(() => controller.Engine.MouseHookActive, "Mouse hook installed");
        Check(controller.AccelerationForcedOff, "Acceleration forced off while scaling");

        controller.SetMaster(false);
        await Settle();
        Check(!controller.MasterEnabled, "Master Enable off");
        await WaitFor(() => !controller.Engine.MouseHookActive, "Mouse hook removed when Master Enable is off");

        controller.SetMaster(true);
        await Settle();
        await WaitFor(() => controller.Engine.MouseHookActive, "Mouse hook reinstalled when Master Enable is on");

        await VisitAllPages("after edits");

        // Close to tray and reopen (the window is recreated).
        Current.Windows.OfType<Views.MainWindow>().FirstOrDefault()?.Close();
        await Settle();
        ShowMainWindow("tester");
        await Settle(600);
        Check(controller.Engine.TraceEnabled, "Input tester tracing active");

        Check(controller.Saver.Flush(force: true), "Settings saved");
        Check(File.Exists(Path.Combine(controller.DataDirectory, Core.Persistence.SettingsStore.FileName)), "settings.json written");
        foreach (string dialog in DialogService.HeadlessLog)
        {
            Note(dialog);
        }
    }

    private async Task VisitAllPages(string label)
    {
        foreach (var item in _mainViewModel!.Pages)
        {
            _mainViewModel.Navigate(item.Page.Key);
            await Settle();
            Check(ReferenceEquals(_mainViewModel.CurrentPage, item.Page), $"Page '{item.Title}' shown ({label})");
        }
    }

    private void Check(bool condition, string what)
    {
        if (condition)
        {
            Note("OK   " + what);
        }
        else
        {
            _selfTestFailures.Add("FAIL " + what);
        }
    }

    private void Note(string text) => _selfTestLog.Add(text);

    private async Task WaitFor(Func<bool> condition, string what)
    {
        for (int i = 0; i < 40 && !condition(); i++)
        {
            await Task.Delay(50);
        }

        Check(condition(), what);
    }

    private async Task Settle(int ms = 250)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await Task.Delay(ms);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
    }

    private void FinishSelfTest()
    {
        if (_selfTestFinished)
        {
            return;
        }

        _selfTestFinished = true;
        var report = new StringBuilder();
        foreach (string line in _selfTestLog)
        {
            report.AppendLine(line);
        }

        foreach (string failure in _selfTestFailures)
        {
            report.AppendLine(failure);
        }

        report.AppendLine(_selfTestFailures.Count == 0 ? "SELF-TEST PASSED" : $"SELF-TEST FAILED ({_selfTestFailures.Count} problem(s))");
        try
        {
            File.WriteAllText(SelfTestOutput!, report.ToString());
        }
        catch (Exception ex)
        {
            Log.Error("Could not write the self-test report.", ex);
        }

        int exitCode = _selfTestFailures.Count == 0 ? 0 : 1;
        if (_controller is not null)
        {
            _controller.Exit(exitCode);
        }
        else
        {
            Shutdown(exitCode);
        }

        try
        {
            Directory.Delete(StoragePaths.OverrideDirectory!, recursive: true);
        }
        catch (Exception)
        {
            // Temporary folder; best effort.
        }
    }

    private sealed class BindingErrorListener : TraceListener
    {
        private readonly List<string> _failures;
        private readonly StringBuilder _pending = new();

        public BindingErrorListener(List<string> failures) => _failures = failures;

        public override void Write(string? message) => _pending.Append(message);

        public override void WriteLine(string? message)
        {
            _pending.Append(message);
            _failures.Add("Binding error: " + _pending);
            _pending.Clear();
        }
    }
}
