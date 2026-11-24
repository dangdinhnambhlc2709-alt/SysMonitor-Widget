using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using WinHardwareMonitor.Models;

namespace WinHardwareMonitor.Services;

public static class Win32Helper
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint LWA_ALPHA = 0x00000002;

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;
    private const int WCA_ACCENT_POLICY = 19;

    private enum DWM_WINDOW_CORNER_PREFERENCE
    {
        DWMWCP_DEFAULT = 0,
        DWMWCP_DONOTROUND = 1,
        DWMWCP_ROUND = 2,
        DWMWCP_ROUNDSMALL = 3
    }

    private enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_INVALID_STATE = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MARGINS
    {
        public int cxLeftWidth;
        public int cxRightWidth;
        public int cyTopHeight;
        public int cyBottomHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref uint attrValue, int attrSize);

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

    public static void InitializeWidgetDwmSurface(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        try
        {
            // BẮT BUỘC: đặt nền DirectX backbuffer của HwndTarget về Transparent (#00000000),
            // nếu không WPF sẽ tô nền đen đặc phía dưới khiến 0% opacity không thể trong suốt!
            if (HwndSource.FromHwnd(handle)?.CompositionTarget != null)
            {
                HwndSource.FromHwnd(handle)!.CompositionTarget!.BackgroundColor = System.Windows.Media.Colors.Transparent;
            }

            var margins = new MARGINS
            {
                cxLeftWidth = -1,
                cxRightWidth = -1,
                cyTopHeight = -1,
                cyBottomHeight = -1
            };
            DwmExtendFrameIntoClientArea(handle, ref margins);

            uint noBorder = DWMWA_COLOR_NONE;
            DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref noBorder, sizeof(uint));
        }
        catch { }
    }

    /// <summary>
    /// Áp dụng 1 trong 3 chế độ hiệu ứng nền Windows 11 tức thì (Tắt làm mờ / Kính mềm / Kính mờ),
    /// đồng bộ với thanh trượt Độ đậm nền (0% -> 100%) và bo góc DWM chuẩn Windows 11.
    /// </summary>
    public static void ApplyWidgetBlurMode(
        Window window,
        WidgetBlurMode mode,
        double cornerRadius,
        double backgroundOpacityPercent,
        bool isLightBackground)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        try
        {
            InitializeWidgetDwmSurface(window);

            // Đảm bảo tắt SystemBackdrop mặc định của Win11 để không xung đột với SetWindowCompositionAttribute
            int noneBackdrop = 1; // DWMSBT_NONE
            DwmSetWindowAttribute(handle, DWMWA_SYSTEMBACKDROP_TYPE, ref noneBackdrop, sizeof(int));

            // 1. Thiết lập bo góc chuẩn Windows 11 ôm khít lớp làm mờ
            int cornerPref;
            if (mode == WidgetBlurMode.Off)
            {
                cornerPref = (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;
            }
            else if (cornerRadius <= 0.5)
            {
                cornerPref = (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DONOTROUND;
            }
            else if (cornerRadius <= 6.0)
            {
                cornerPref = (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUNDSMALL;
            }
            else
            {
                cornerPref = (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
            }
            DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

            // 2. Thiết lập chính sách làm mờ DWM
            double opRatio = Math.Clamp(backgroundOpacityPercent / 100.0, 0.0, 1.0);
            var accent = new AccentPolicy();

            switch (mode)
            {
                case WidgetBlurMode.SoftGlass:
                    // Kính mềm (Liquid Glass / Soft Glass): BlurBehind nhẹ giữ đường nét phía sau mềm mại, trong trẻo
                    accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND;
                    accent.AccentFlags = 0;
                    accent.GradientColor = 0;
                    break;

                case WidgetBlurMode.FrostedGlass:
                    // Kính mờ (Frosted Glass / Acrylic): tán nhòe Gaussian sâu thành mảng màu chuyển sắc mịn
                    // Alpha tối thiểu = 1 (0x01) để Win11 kích hoạt Acrylic mà vẫn trong sạch khi kéo thanh trượt về 0%
                    byte acrylicAlpha = (byte)Math.Clamp(Math.Round(1 + opRatio * 115.0), 1, 140);
                    uint rgbBgr = isLightBackground ? 0x00FAF7F5u : 0x001E1916u;
                    accent.AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;
                    accent.AccentFlags = 2;
                    accent.GradientColor = ((uint)acrylicAlpha << 24) | rgbBgr;
                    break;

                default:
                    // Tắt làm mờ hoàn toàn: nền 0% sẽ trong suốt 100% tuyệt đối
                    accent.AccentState = AccentState.ACCENT_DISABLED;
                    accent.AccentFlags = 0;
                    accent.GradientColor = 0;
                    break;
            }

            int structSize = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(structSize);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WindowCompositionAttributeData
                {
                    Attribute = WCA_ACCENT_POLICY,
                    SizeOfData = structSize,
                    Data = ptr
                };
                SetWindowCompositionAttribute(handle, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch { }
    }

    public static void ApplyWidgetWindowStyles(Window window, bool clickThrough)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        int exStyle = GetWindowLong(handle, GWL_EXSTYLE);
        exStyle |= WS_EX_TOOLWINDOW;

        if (clickThrough)
        {
            exStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT;
            SetWindowLong(handle, GWL_EXSTYLE, exStyle);
            SetLayeredWindowAttributes(handle, 0, 255, LWA_ALPHA);
        }
        else
        {
            exStyle &= ~(WS_EX_TRANSPARENT | WS_EX_LAYERED);
            SetWindowLong(handle, GWL_EXSTYLE, exStyle);
        }
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
    private const uint VK_L = 0x4C;
    private const uint VK_R = 0x52;
    private const int HOTKEY_LOCK_ID = 0x5748;
    private const int HOTKEY_RESET_FPS_ID = 0x5749;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private IntPtr _handle;
    private bool _lockRegistered;
    private bool _resetFpsRegistered;

    public bool IsLockHotkeyRegistered => _lockRegistered;
    public bool IsResetFpsHotkeyRegistered => _resetFpsRegistered;

    public event Action? ToggleClickThroughPressed;
    public event Action? ResetFpsPressed;

    public bool Register(Window hostWindow)
    {
        Unregister();

        var helper = new WindowInteropHelper(hostWindow);
        _handle = helper.EnsureHandle();
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(WndProc);

        _lockRegistered = RegisterHotKey(_handle, HOTKEY_LOCK_ID, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_L);
        _resetFpsRegistered = RegisterHotKey(_handle, HOTKEY_RESET_FPS_ID, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_R);
        return _lockRegistered;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HOTKEY_LOCK_ID)
            {
                ToggleClickThroughPressed?.Invoke();
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
            if (_lockRegistered)
            {
                UnregisterHotKey(_handle, HOTKEY_LOCK_ID);
                _lockRegistered = false;
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
