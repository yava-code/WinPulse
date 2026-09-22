namespace Pulse.Windows;

using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using Pulse.Core.Models;
using Pulse.Core.Providers;
using Pulse.Core.Security;
using Pulse.Core.Services;
using Pulse.Windows.Platform;
using Pulse.Windows.SystemIntegration;
using Pulse.Windows.Views;

public partial class App : Application
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);
    private const int ATTACH_PARENT_PROCESS = -1;
    private const int STD_OUTPUT_HANDLE = -11;

    private static System.Threading.Mutex? _singleInstanceMutex;
    private static System.Threading.EventWaitHandle? _activateEvent;

    private TrayIconManager? _trayIconManager;
    private FloatingRailWindow? _railWindow;
    private SettingsWindow? _settingsWindow;
    private AppSettings _appSettings = null!;
    private UsageStore? _usageStore;
    private UsageCache? _cache;
    private ICredentialStore? _credentials;
    private AntigravityUsageService? _antigravityService;
    private GoogleOAuthService? _googleOAuthService;
    private GitHubCopilotOAuthService? _githubOAuthService;

    private static bool _isConsoleAttached = false;

    private static void TryAttachConsole()
    {
        try
        {
            if (AttachConsole(ATTACH_PARENT_PROCESS))
            {
                _isConsoleAttached = true;
                var stdOutStream = Console.OpenStandardOutput();
                Console.SetOut(new System.IO.StreamWriter(stdOutStream, new System.Text.UTF8Encoding(false)) { AutoFlush = true });
                var stdErrStream = Console.OpenStandardError();
                Console.SetError(new System.IO.StreamWriter(stdErrStream, new System.Text.UTF8Encoding(false)) { AutoFlush = true });
            }
        }
        catch { }
    }

    private static void LogStartup(string message)
    {
        try
        {
            var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            if (_isConsoleAttached)
            {
                Console.WriteLine(line);
            }
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "startup.log"), line + "\n");
        }
        catch { }
    }

    public static App? CurrentInstance { get; private set; }

    private static void KillOtherInstances()
    {
        try
        {
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var currentPid = currentProcess.Id;
            var procNames = new[] { currentProcess.ProcessName, "Pulse.Windows" };
            foreach (var name in procNames.Distinct())
            {
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
                {
                    if (p.Id != currentPid)
                    {
                        try
                        {
                            LogStartup($"Terminating previous instance PID {p.Id} ({p.ProcessName}) before launch...");
                            p.Kill(true);
                            p.WaitForExit(1500);
                        }
                        catch { }
                    }
                }
            }
        }
        catch { }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CurrentInstance = this;

        // 0. Terminate any previous or hung instances before launch
        KillOtherInstances();

        if (e.Args.Contains("--debug") || e.Args.Contains("-d") || e.Args.Contains("--console"))
        {
            TryAttachConsole();
            Console.WriteLine("==================================================");
            Console.WriteLine(" Pulse Windows - Console Debug Mode");
            Console.WriteLine("==================================================");
        }

        LogStartup($"Application starting up. Args: {string.Join(" ", e.Args)}");

        // Load application settings & apply theme immediately
        _appSettings = AppSettings.Load();
        ThemeManager.ApplyTheme(_appSettings.Theme, _appSettings.UsesGlass);
        LogStartup($"Settings loaded. Theme: {_appSettings.Theme}, Glass: {_appSettings.UsesGlass}, PanelVisible: {_appSettings.IsPanelVisible}, AutoCollapse: {_appSettings.AutoCollapse}");

        // Enforce single-instance application: activate existing instance if re-launched
        bool createdNew;
        try
        {
            _singleInstanceMutex = new System.Threading.Mutex(true, "Pulse_Windows_SingleInstance_App", out createdNew);
        }
        catch (AbandonedMutexException)
        {
            LogStartup("Acquired abandoned single-instance mutex from terminated process");
            createdNew = true;
        }
        catch (Exception ex)
        {
            LogStartup($"Mutex creation error: {ex.Message}");
            createdNew = true;
        }

        if (!createdNew)
        {
            LogStartup("Secondary instance detected. Signaling existing instance and exiting.");
            if (_isConsoleAttached)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[Pulse] Notice: Another instance of Pulse is ALREADY RUNNING in the background / system tray.");
                Console.WriteLine("[Pulse] Sent activation signal to the running instance to bring Settings window to the front.");
                Console.WriteLine("[Pulse] (To terminate the existing background process, run: taskkill /F /IM Pulse.Windows.exe)");
                Console.ResetColor();
            }
            try
            {
                using var evt = System.Threading.EventWaitHandle.OpenExisting("Pulse_Windows_Activate_Event");
                evt.Set();
            }
            catch { }
            Environment.Exit(0);
            return;
        }

        LogStartup("Primary single instance established.");

        try
        {
            _activateEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, "Pulse_Windows_Activate_Event");
            Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        _activateEvent.WaitOne();
                        Dispatcher.Invoke(() =>
                        {
                            LogStartup("Activation event received. Restoring windows.");
                            OpenSettings();
                            if (_railWindow != null)
                            {
                                if (!_railWindow.IsVisible) _railWindow.Show();
                                _railWindow.ExpandRailTemporarily();
                            }
                        });
                    }
                    catch { break; }
                }
            });
        }
        catch (Exception ex)
        {
            LogStartup($"Failed to initialize activation event: {ex.Message}");
        }

        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            if (_isConsoleAttached)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[CRITICAL] Unhandled Exception: {ev.ExceptionObject}");
                Console.ResetColor();
            }
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.UtcNow}] {ev.ExceptionObject}\n");
            }
            catch { }
        };
        DispatcherUnhandledException += (s, ev) =>
        {
            if (_isConsoleAttached)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] Dispatcher Exception: {ev.Exception}");
                Console.ResetColor();
            }
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "crash.log"), $"[{DateTime.UtcNow}] {ev.Exception}\n");
            }
            catch { }
            ev.Handled = true;
        };

        var paths = new WindowsPlatformPaths();
        _cache = new UsageCache(paths);

        // Check if --json argument was passed
        if (e.Args.Contains("--json"))
        {
            var cached = _cache.ReadRaw();
            if (string.IsNullOrWhiteSpace(cached))
            {
                cached = UsageReport.GenerateJson(new Dictionary<AccountKey, ProviderUsage>());
            }

            try
            {
                using var stdOutStream = Console.OpenStandardOutput();
                using var writer = new System.IO.StreamWriter(stdOutStream, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
                writer.WriteLine(cached);
            }
            catch
            {
                try
                {
                    AttachConsole(ATTACH_PARENT_PROCESS);
                    Console.WriteLine(cached);
                }
                catch { }
            }

            Environment.Exit(0);
            return;
        }

        _credentials = new WindowsDPAPICredentialStore(paths);
        _antigravityService = new AntigravityUsageService(paths, _credentials);
        var copilotService = new CopilotUsageService(_credentials, paths);
        _googleOAuthService = new GoogleOAuthService(_credentials);
        _githubOAuthService = new GitHubCopilotOAuthService(_credentials);

        var antigravityService = _antigravityService;
        var credentials = _credentials;
        var googleOAuthService = _googleOAuthService;
        var githubOAuthService = _githubOAuthService;

        var providers = new List<IUsageProviderService>
        {
            antigravityService,
            new CodexUsageService(paths),
            new ClaudeCodeUsageService(paths),
            new CursorUsageService(paths),
            copilotService,
            new DeepSeekUsageService(credentials),
            new GrokUsageService(paths, credentials),
            new OpenCodeGoUsageService(paths, credentials),
            new KimiCodeUsageService(paths, credentials),
            new CommandCodeUsageService(paths, credentials),
            new DevinUsageService(paths, credentials),
            new MiniMaxUsageService(Provider.MiniMax, paths, credentials),
            new OllamaCloudUsageService(paths, credentials)
        };

        _usageStore = new UsageStore(providers);

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Dynamic account discovery: discover all accounts from local files/DPAPI instantly
        var discoveredAg = antigravityService.DiscoverAccountsLocal();
        var defaultAccounts = new List<AccountKey>();
        if (discoveredAg != null && discoveredAg.Count > 0)
        {
            defaultAccounts.AddRange(discoveredAg);
        }
        else
        {
            defaultAccounts.Add(new AccountKey(Provider.Antigravity));
        }

        defaultAccounts.Add(new AccountKey(Provider.ClaudeCode));
        defaultAccounts.Add(new AccountKey(Provider.Cursor));
        defaultAccounts.Add(new AccountKey(Provider.Codex));
        defaultAccounts.Add(new AccountKey(Provider.Copilot));

        _usageStore.SetMonitoredAccounts(defaultAccounts);

        // 1. Initialize tray icon FIRST so user always has the tray icon & exit option available
        LogStartup("Initializing TrayIconManager");
        _trayIconManager = new TrayIconManager(
            _usageStore,
            onOpenSettings: OpenSettings,
            onTogglePanel: ToggleRailVisibility,
            onExit: ExitApplication
        );

        // 2. Initialize floating rail window
        LogStartup("Initializing FloatingRailWindow");
        _railWindow = new FloatingRailWindow(
            _usageStore,
            _appSettings,
            onOpenSettings: OpenSettings,
            onExit: ExitApplication
        );

        _appSettings.SettingsChanged += () =>
        {
            _railWindow?.ApplySettings(_appSettings);
        };

        _usageStore.UsageUpdated += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                _railWindow?.UpdateReadings(_usageStore.Readings);
                _cache?.Save(_usageStore.Readings);
            });
        };

        _usageStore.RefreshCompleted += () =>
        {
            Dispatcher.Invoke(() =>
            {
                _railWindow?.UpdateReadings(_usageStore.Readings);
                _cache?.Save(_usageStore.Readings);
            });
        };

        // Show floating dock immediately
        LogStartup("Showing FloatingRailWindow");
        _railWindow.Show();
        _railWindow.UpdateReadings(_usageStore.Readings);

        // Notify user via tray balloon
        _trayIconManager.ShowStartupNotification();

        // 3. Initialize & show Settings window
        try
        {
            LogStartup("Creating SettingsWindow");
            CreateSettingsWindow();
            if (!e.Args.Contains("--daemon") && !e.Args.Contains("--minimized"))
            {
                LogStartup("Opening SettingsWindow on launch");
                OpenSettings();
            }
        }
        catch (Exception ex)
        {
            LogStartup($"Failed to open Settings window on startup: {ex}");
        }

        LogStartup("Startup sequence completed successfully.");

        // Start background refresh timer
        _usageStore.Start(TimeSpan.FromMinutes(5));

        // Background non-blocking live refresh
        _ = Task.Run(async () =>
        {
            await _usageStore.RefreshAllAsync().ConfigureAwait(false);
        });
    }

    private void OpenSettings()
    {
        try
        {
            if (_settingsWindow == null)
            {
                CreateSettingsWindow();
            }

            if (_settingsWindow != null)
            {
                if (_settingsWindow.WindowState == WindowState.Minimized)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                }
                _settingsWindow.Show();
                _settingsWindow.Activate();
                _settingsWindow.Focus();

                // Briefly toggle Topmost to guarantee window surfaces above full-screen editors
                _settingsWindow.Topmost = true;
                _settingsWindow.Topmost = false;

                Win32WindowHelper.BringToFront(_settingsWindow);
            }
        }
        catch (Exception ex)
        {
            LogStartup($"Error in OpenSettings: {ex.Message}");
            try
            {
                CreateSettingsWindow();
                if (_settingsWindow != null)
                {
                    _settingsWindow.WindowState = WindowState.Normal;
                    _settingsWindow.Show();
                    _settingsWindow.Activate();
                    Win32WindowHelper.BringToFront(_settingsWindow);
                }
            }
            catch { }
        }
    }

    private void CreateSettingsWindow()
    {
        if (_usageStore == null || _credentials == null || _googleOAuthService == null || _githubOAuthService == null || _antigravityService == null)
        {
            return;
        }

        _settingsWindow = new SettingsWindow(
            _usageStore,
            _credentials,
            _googleOAuthService,
            _githubOAuthService,
            _antigravityService,
            _appSettings,
            onSettingsSaved: () =>
            {
                _railWindow?.ApplySettings(_appSettings);
                _ = _usageStore.RefreshAllAsync();
            }
        );
    }

    private void ToggleRailVisibility()
    {
        if (_railWindow == null) return;
        _appSettings.IsPanelVisible = !_railWindow.IsVisible;
        _appSettings.Save();
        _railWindow.ApplySettings(_appSettings);
    }

    public static void ExitApplication()
    {
        LogStartup("ExitApplication called. Terminating process immediately.");
        try
        {
            CurrentInstance?._trayIconManager?.Dispose();
        }
        catch { }

        try
        {
            _activateEvent?.Dispose();
            _singleInstanceMutex?.ReleaseMutex();
            _singleInstanceMutex?.Dispose();
        }
        catch { }

        try
        {
            System.Diagnostics.Process.GetCurrentProcess().Kill(true);
        }
        catch
        {
            Environment.Exit(0);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _trayIconManager?.Dispose();
            _activateEvent?.Dispose();
            _singleInstanceMutex?.Dispose();
        }
        catch { }
        base.OnExit(e);
    }
}
