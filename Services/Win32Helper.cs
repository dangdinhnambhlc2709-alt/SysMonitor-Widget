using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using WinHardwareMonitor.Models;
using Forms = System.Windows.Forms;

namespace WinHardwareMonitor.Services;

/// <summary>
/// Các hàm Win32 API gọn nhẹ cho Overlay OSD thuần túy (100% xuyên chuột, không chiếm tiêu điểm),
/// neo vị trí đa màn hình, đọc RAM/tiến trình foreground và khởi chạy cùng Windows.
/// </summary>
public static class Win32Helper
{
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_MOUSEACTIVATE = 0x0021;
    public static readonly IntPtr HTTRANSPARENT = new(-1);
    public static readonly IntPtr MA_NOACTIVATE = new(3);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>
    /// Áp dụng cố định các cờ Win32 cho Overlay OSD chuyên dụng (xuyên chuột 100%, không chiếm tiêu điểm game).
    /// </summary>
    public static void ApplyPureOsdWindowStyles(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
        exStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        SetWindowLong(handle, GWL_EXSTYLE, exStyle);
    }

    public static List<MonitorOption> EnumerateMonitors()
    {
        var result = new List<MonitorOption>();
        try
        {
            var screens = Forms.Screen.AllScreens;
            for (int i = 0; i < screens.Length; i++)
            {
                var s = screens[i];
                result.Add(new MonitorOption
                {
                    DeviceName = s.DeviceName,
                    Index = i,
                    WidthPx = s.Bounds.Width,
                    HeightPx = s.Bounds.Height,
                    IsPrimary = s.Primary
                });
            }
        }
        catch { }

        if (result.Count == 0)
        {
            result.Add(new MonitorOption
            {
                DeviceName = string.Empty,
                Index = 0,
                WidthPx = (int)SystemParameters.PrimaryScreenWidth,
                HeightPx = (int)SystemParameters.PrimaryScreenHeight,
                IsPrimary = true
            });
        }
        return result;
    }

    /// <summary>
    /// Tính toán vị trí (Left, Top theo DIP của WPF) cho cửa sổ Overlay theo 9 điểm neo,
    /// căn lề X/Y (px) và màn hình đã chọn.
    /// </summary>
    public static void PositionOverlayWindow(
        Window window,
        string? monitorDeviceName,
        OverlayAnchor anchor,
        double offsetX,
        double offsetY)
    {
        try
        {
            var screens = Forms.Screen.AllScreens;
            Forms.Screen target = screens.FirstOrDefault(s =>
                                      !string.IsNullOrWhiteSpace(monitorDeviceName) &&
                                      s.DeviceName.Equals(monitorDeviceName, StringComparison.OrdinalIgnoreCase))
                                  ?? Forms.Screen.PrimaryScreen
                                  ?? screens.FirstOrDefault()!;

            if (target == null)
                return;

            double scaleX = 1.0;
            double scaleY = 1.0;
            var source = PresentationSource.FromVisual(window);
            if (source?.CompositionTarget != null)
            {
                var m = source.CompositionTarget.TransformToDevice;
                if (m.M11 > 0.1) scaleX = m.M11;
                if (m.M22 > 0.1) scaleY = m.M22;
            }
            else
            {
                var dpi = VisualTreeHelper.GetDpi(window);
                if (dpi.DpiScaleX > 0.1) scaleX = dpi.DpiScaleX;
                if (dpi.DpiScaleY > 0.1) scaleY = dpi.DpiScaleY;
            }

            double monLeft = target.Bounds.Left / scaleX;
            double monTop = target.Bounds.Top / scaleY;
            double monWidth = target.Bounds.Width / scaleX;
            double monHeight = target.Bounds.Height / scaleY;

            double winW = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            double winH = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
            if (double.IsNaN(winW)) winW = 0;
            if (double.IsNaN(winH)) winH = 0;

            double ox = Math.Max(0, offsetX);
            double oy = Math.Max(0, offsetY);

            double left = anchor switch
            {
                OverlayAnchor.TopLeft or OverlayAnchor.MiddleLeft or OverlayAnchor.BottomLeft =>
                    monLeft + ox,
                OverlayAnchor.TopCenter or OverlayAnchor.MiddleCenter or OverlayAnchor.BottomCenter =>
                    monLeft + (monWidth - winW) / 2.0 + ox - 16,
                _ => monLeft + monWidth - winW - ox
            };

            // Với điểm neo giữa theo trục ngang/dọc, dùng offset làm dịch chuyển tương đối quanh tâm nếu > 0?
            // Để trực quan: điểm neo Giữa căn chính giữa + dịch nhẹ khi cần, nhưng giữ hành vi nhất quán:
            if (anchor is OverlayAnchor.TopCenter or OverlayAnchor.MiddleCenter or OverlayAnchor.BottomCenter)
                left = monLeft + (monWidth - winW) / 2.0;

            double top = anchor switch
            {
                OverlayAnchor.TopLeft or OverlayAnchor.TopCenter or OverlayAnchor.TopRight =>
                    monTop + oy,
                OverlayAnchor.MiddleLeft or OverlayAnchor.MiddleCenter or OverlayAnchor.MiddleRight =>
                    monTop + (monHeight - winH) / 2.0,
                _ => monTop + monHeight - winH - oy
            };

            if (Math.Abs(window.Left - left) > 0.5)
                window.Left = Math.Round(left);
            if (Math.Abs(window.Top - top) > 0.5)
                window.Top = Math.Round(top);
        }
        catch { }
    }

    public static bool TryGetForegroundProcess(out int pid, out string processName)
    {
        pid = 0;
        processName = string.Empty;

        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
                return false;

            _ = GetWindowThreadProcessId(hwnd, out uint rawPid);
            if (rawPid == 0)
                return false;

            pid = (int)rawPid;
            using var proc = Process.GetProcessById(pid);
            processName = proc.ProcessName ?? string.Empty;
            return !string.IsNullOrWhiteSpace(processName);
        }
        catch
        {
            return false;
        }
    }

    public static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryGetPhysicalMemory(out double usedGb, out double totalGb, out double percent)
    {
        usedGb = 0;
        totalGb = 0;
        percent = 0;

        var status = new MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
        };

        if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
            return false;

        const double bytesPerGb = 1024.0 * 1024.0 * 1024.0;
        totalGb = status.ullTotalPhys / bytesPerGb;
        double availGb = status.ullAvailPhys / bytesPerGb;
        usedGb = Math.Max(0, totalGb - availGb);
        percent = status.dwMemoryLoad;
        return true;
    }

    private const string StartupTaskName = "WinHardwareMonitor_AutoStart";
    private const string StartupRegValueName = "WinHardwareMonitor";

    public static bool SetWindowsStartup(bool enable, out string? errorMessage)
    {
        errorMessage = null;
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
        {
            errorMessage = LocalizationService.Instance["Win32ExePathMissing"];
            return false;
        }

        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            runKey?.DeleteValue(StartupRegValueName, throwOnMissingValue: false);
        }
        catch { }

        if (IsRunningAsAdministrator())
        {
            try
            {
                string args = enable
                    ? $"/Create /TN \"{StartupTaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F"
                    : $"/Delete /TN \"{StartupTaskName}\" /F";

                var psi = new ProcessStartInfo("schtasks.exe", args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(4000);

                if (!enable || proc?.ExitCode == 0)
                    return true;

                errorMessage = proc?.StandardError.ReadToEnd()?.Trim();
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
            }
        }

        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (runKey != null)
            {
                if (enable)
                    runKey.SetValue(StartupRegValueName, $"\"{exePath}\"");
                else
                    runKey.DeleteValue(StartupRegValueName, throwOnMissingValue: false);
                return true;
            }
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }

        return false;
    }

    public static bool IsWindowsStartupEnabled()
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{StartupTaskName}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(2000);
            if (proc?.ExitCode == 0)
                return true;
        }
        catch { }

        try
        {
            using var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
            return runKey?.GetValue(StartupRegValueName) != null;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class GlobalHotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_O = 0x4F;
    private const uint VK_R = 0x52;
    private const int HOTKEY_TOGGLE_OVERLAY_ID = 0x5748;
    private const int HOTKEY_RESET_FPS_ID = 0x5749;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private IntPtr _handle;
    private bool _toggleOverlayRegistered;
    private bool _resetFpsRegistered;

    public bool IsToggleOverlayHotkeyRegistered => _toggleOverlayRegistered;
    public bool IsResetFpsHotkeyRegistered => _resetFpsRegistered;

    public event Action? ToggleOverlayPressed;
    public event Action? ResetFpsPressed;

    public bool Register(Window hostWindow, bool enableToggleOverlayHotkey)
    {
        Unregister();

        var helper = new WindowInteropHelper(hostWindow);
        _handle = helper.EnsureHandle();
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);

        if (enableToggleOverlayHotkey)
            _toggleOverlayRegistered = RegisterHotKey(_handle, HOTKEY_TOGGLE_OVERLAY_ID, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_O);

        _resetFpsRegistered = RegisterHotKey(_handle, HOTKEY_RESET_FPS_ID, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_R);
        return !enableToggleOverlayHotkey || _toggleOverlayRegistered;
    }

    public bool SetToggleOverlayHotkeyEnabled(bool enable)
    {
        if (_handle == IntPtr.Zero)
            return false;

        if (enable)
        {
            if (!_toggleOverlayRegistered)
                _toggleOverlayRegistered = RegisterHotKey(_handle, HOTKEY_TOGGLE_OVERLAY_ID, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_O);
            return _toggleOverlayRegistered;
        }
        else
        {
            if (_toggleOverlayRegistered)
            {
                UnregisterHotKey(_handle, HOTKEY_TOGGLE_OVERLAY_ID);
                _toggleOverlayRegistered = false;
            }
            return true;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_TOGGLE_OVERLAY_ID)
            {
                ToggleOverlayPressed?.Invoke();
                handled = true;
            }
            else if (id == HOTKEY_RESET_FPS_ID)
            {
                ResetFpsPressed?.Invoke();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    public void Unregister()
    {
        if (_handle != IntPtr.Zero)
        {
            if (_toggleOverlayRegistered)
            {
                UnregisterHotKey(_handle, HOTKEY_TOGGLE_OVERLAY_ID);
                _toggleOverlayRegistered = false;
            }
            if (_resetFpsRegistered)
            {
                UnregisterHotKey(_handle, HOTKEY_RESET_FPS_ID);
                _resetFpsRegistered = false;
            }
        }

        if (_source != null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }

    public void Dispose()
    {
        Unregister();
    }
}
