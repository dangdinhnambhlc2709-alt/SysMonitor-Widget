using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WinHardwareMonitor.Services;
using WinHardwareMonitor.ViewModels;
using WinHardwareMonitor.Views;
using Forms = System.Windows.Forms;

namespace WinHardwareMonitor;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private ConfigService? _configService;
    private HardwareMonitorService? _hardwareService;
    private PresentMonService? _presentMonService;
    private GpuTechDetectorService? _gpuTechDetectorService;
    private UpdateService? _updateService;
    private GlobalHotkeyManager? _hotkeyManager;
    private MainViewModel? _viewModel;
    private SettingsWindow? _settingsWindow;
    private WidgetWindow? _widgetWindow;
    private UpdateWindow? _updateWindow;
    private Forms.NotifyIcon? _trayIcon;
    private Forms.ToolStripMenuItem? _trayOpenItem;
    private Forms.ToolStripMenuItem? _trayLockItem;
    private Forms.ToolStripMenuItem? _trayTopmostItem;
    private Forms.ToolStripMenuItem? _trayVisibilityItem;
    private Forms.ToolStripMenuItem? _trayResetFpsItem;
    private Forms.ToolStripMenuItem? _trayExitItem;
    private bool _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);
            MessageBox.Show(
                LocalizationService.Instance.Format("MsgUnexpectedError", args.Exception.Message),
                LocalizationService.Instance["MsgErrorTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                LogCrash(ex);
        };

        _singleInstanceMutex = new Mutex(true, "WinHardwareMonitor_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                LocalizationService.Instance["MsgAlreadyRunning"],
                LocalizationService.Instance["AppTitle"],
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        _configService = new ConfigService();
        _hardwareService = new HardwareMonitorService();
        _hardwareService.Initialize();
        _presentMonService = new PresentMonService();
        _gpuTechDetectorService = new GpuTechDetectorService();
        _updateService = new UpdateService();

        _viewModel = new MainViewModel(_configService, _hardwareService, _presentMonService, _gpuTechDetectorService, _updateService);

        _widgetWindow = new WidgetWindow(_viewModel);
        _settingsWindow = new SettingsWindow(_viewModel);

        _settingsWindow.Closing += (_, args) =>
        {
            if (!_isExiting)
            {
                if (_viewModel.MinimizeToTrayOnClose)
                {
                    args.Cancel = true;
                    _settingsWindow.Hide();
                }
                else
                {
                    args.Cancel = true;
                    Dispatcher.BeginInvoke(ExitApplication);
                }
            }
        };

        _viewModel.OpenSettingsRequested += ShowSettingsWindow;
        _viewModel.ExitAppRequested += ExitApplication;
        _viewModel.ClickThroughChanged += locked =>
        {
            UpdateTrayMenuState();
            _widgetWindow?.ApplyClickThrough(locked);
        };
        _viewModel.WidgetTopmostChanged += topmost =>
        {
            UpdateTrayMenuState();
            if (_widgetWindow != null)
                _widgetWindow.Topmost = topmost;
        };
        _viewModel.WidgetVisibilityChanged += visible =>
        {
            UpdateTrayMenuState();
            if (_widgetWindow == null) return;
            if (visible) _widgetWindow.Show();
            else _widgetWindow.Hide();
        };
        _viewModel.CenterWidgetRequested += () => _widgetWindow?.CenterOnPrimaryScreen();
        _viewModel.BlurSettingsChanged += () => _widgetWindow?.RefreshDwmBlurEffect();
        _viewModel.UpdateAvailableDetected += release =>
            Dispatcher.BeginInvoke(() => ShowUpdateWindow(release));
        _viewModel.ResetFpsToastRequested += () =>
            _widgetWindow?.ShowStatusToast(LocalizationService.Instance["ToastResetFps"]);

        // 1. Cảm biến phần cứng mỗi 1 giây
        _hardwareService.SnapshotReady += snapshot =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                _viewModel?.ApplySensorSnapshot(snapshot);
            });
        };

        // 2. FPS & Đồ thị mỗi 500ms từ Intel PresentMon 2.x (ETW)
        _presentMonService.SnapshotReady += fpsSnap =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                _viewModel?.ApplyFpsSnapshot(fpsSnap);
            });
        };

        // 3. Nhận diện công nghệ GPU mỗi 2.5 giây (an toàn Anti-Cheat)
        _gpuTechDetectorService.SnapshotReady += gpuTechSnap =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                _viewModel?.ApplyGpuTechDetection(gpuTechSnap);
            });
        };

        SetupTrayIcon();
        LocalizationService.Instance.LanguageChanged += () => Dispatcher.BeginInvoke(UpdateTrayMenuState);

        if (_viewModel.IsWidgetVisible)
            _widgetWindow.Show();
        else
        {
            _widgetWindow.Show();
            _widgetWindow.Hide();
        }

        _hotkeyManager = new GlobalHotkeyManager();
        _hotkeyManager.ToggleClickThroughPressed += () =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_viewModel == null) return;
                _viewModel.IsClickThrough = !_viewModel.IsClickThrough;
                _widgetWindow?.ShowStatusToast(_viewModel.IsClickThrough
                    ? LocalizationService.Instance["ToastLocked"]
                    : LocalizationService.Instance["ToastUnlocked"]);
            });
        };
        _hotkeyManager.ResetFpsPressed += () =>
        {
            Dispatcher.BeginInvoke(() => _viewModel?.ResetFpsStats());
        };
        _viewModel.IsHotkeyRegistered = _hotkeyManager.Register(_widgetWindow);

        _settingsWindow.Show();
        _settingsWindow.Activate();

        _hardwareService.Start();
        _presentMonService.Start();
        _gpuTechDetectorService.Start();

        // Tự động kiểm tra bản cập nhật ngầm sau khi mở ứng dụng 3 giây
        if (_viewModel.AutoCheckUpdatesOnStartup)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                    if (!_isExiting && _viewModel != null)
                        await _viewModel.CheckForUpdatesSilentAsync().ConfigureAwait(false);
                }
                catch { }
            });
        }
    }

    private void SetupTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();

        _trayOpenItem = new Forms.ToolStripMenuItem();
        _trayOpenItem.Font = new Font(_trayOpenItem.Font, System.Drawing.FontStyle.Bold);
        _trayOpenItem.Click += (_, _) => Dispatcher.Invoke(ShowSettingsWindow);
        menu.Items.Add(_trayOpenItem);

        _trayVisibilityItem = new Forms.ToolStripMenuItem();
        _trayVisibilityItem.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            if (_viewModel != null)
                _viewModel.IsWidgetVisible = !_viewModel.IsWidgetVisible;
        });
        menu.Items.Add(_trayVisibilityItem);

        _trayLockItem = new Forms.ToolStripMenuItem();
        _trayLockItem.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            if (_viewModel != null)
                _viewModel.IsClickThrough = !_viewModel.IsClickThrough;
        });
        menu.Items.Add(_trayLockItem);

        _trayTopmostItem = new Forms.ToolStripMenuItem
        {
            CheckOnClick = false
        };
        _trayTopmostItem.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            if (_viewModel != null)
                _viewModel.IsWidgetTopmost = !_viewModel.IsWidgetTopmost;
        });
        menu.Items.Add(_trayTopmostItem);

        _trayResetFpsItem = new Forms.ToolStripMenuItem();
        _trayResetFpsItem.Click += (_, _) => Dispatcher.Invoke(() => _viewModel?.ResetFpsStats());
        menu.Items.Add(_trayResetFpsItem);

        menu.Items.Add(new Forms.ToolStripSeparator());

        _trayExitItem = new Forms.ToolStripMenuItem();
        _trayExitItem.Click += (_, _) => Dispatcher.Invoke(ExitApplication);
        menu.Items.Add(_trayExitItem);

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = CreateAppIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowSettingsWindow);

        UpdateTrayMenuState();
    }

    private void UpdateTrayMenuState()
    {
        if (_viewModel == null) return;
        var loc = LocalizationService.Instance;

        if (_trayOpenItem != null)
            _trayOpenItem.Text = loc["TrayOpenSettings"];

        if (_trayVisibilityItem != null)
            _trayVisibilityItem.Text = _viewModel.IsWidgetVisible ? loc["BtnHideWidget"] : loc["BtnShowWidget"];

        if (_trayLockItem != null)
            _trayLockItem.Text = _viewModel.IsClickThrough ? loc["TrayUnlock"] : loc["TrayLock"];

        if (_trayTopmostItem != null)
        {
            _trayTopmostItem.Text = loc["TrayTopmost"];
            _trayTopmostItem.Checked = _viewModel.IsWidgetTopmost;
        }

        if (_trayResetFpsItem != null)
            _trayResetFpsItem.Text = loc["TrayResetFps"];

        if (_trayExitItem != null)
            _trayExitItem.Text = loc["BtnExitApp"];

        if (_trayIcon != null)
        {
            string appTitle = loc["AppTitle"];
            _trayIcon.Text = appTitle.Length > 63 ? appTitle[..63] : appTitle;
        }
    }

    private static Icon CreateAppIcon()
    {
        try
        {
            var res = GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (res?.Stream != null)
            {
                return new Icon(res.Stream);
            }
        }
        catch { }

        return SystemIcons.Application;
    }

    private void ShowUpdateWindow(UpdateReleaseInfo release)
    {
        if (_updateService == null || _isExiting)
            return;

        if (_updateWindow != null)
        {
            _updateWindow.Activate();
            return;
        }

        _updateWindow = new UpdateWindow(_updateService, release, PrepareForInstallerUpgrade);
        _updateWindow.Closed += (_, _) => _updateWindow = null;
        _updateWindow.Show();
        _updateWindow.Activate();
    }

    /// <summary>
    /// Đóng sạch PresentMon, cảm biến, hotkey và khay hệ thống trước khi khởi chạy Setup.exe cài đè.
    /// </summary>
    private void PrepareForInstallerUpgrade()
    {
        _isExiting = true;
        try { _viewModel?.SaveConfigNow(); } catch { }
        try { _hotkeyManager?.Dispose(); } catch { }
        try { _presentMonService?.Dispose(); } catch { }
        try { _gpuTechDetectorService?.Dispose(); } catch { }
        try { _hardwareService?.Dispose(); } catch { }
        try { _configService?.Dispose(); } catch { }

        if (_trayIcon != null)
        {
            try
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            catch { }
            _trayIcon = null;
        }

        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        try { _singleInstanceMutex?.Dispose(); } catch { }
        _singleInstanceMutex = null;
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow == null) return;
        if (!_settingsWindow.IsVisible)
            _settingsWindow.Show();
        if (_settingsWindow.WindowState == WindowState.Minimized)
            _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Activate();
    }

    private void ExitApplication()
    {
        if (_isExiting) return;
        _isExiting = true;

        try { _viewModel?.SaveConfigNow(); } catch { }

        _hotkeyManager?.Dispose();
        _gpuTechDetectorService?.Dispose();
        _presentMonService?.Dispose();
        _hardwareService?.Dispose();
        _configService?.Dispose();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _widgetWindow?.Close();
        _settingsWindow?.Close();

        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();

        Shutdown();
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinHardwareMonitor");
            System.IO.Directory.CreateDirectory(dir);
            string logPath = System.IO.Path.Combine(dir, "error.log");
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]\n{ex}\n\n");
        }
        catch { }
    }
}
