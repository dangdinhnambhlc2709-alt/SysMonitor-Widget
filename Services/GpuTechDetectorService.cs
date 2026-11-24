using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace WinHardwareMonitor.Services;

public enum GpuDetectionStatusKind
{
    Waiting,
    DirectDll,
    AntiCheatFallback,
    GameFolder
}

public sealed class GpuTechDetectionSnapshot
{
    public bool IsGameActive { get; init; }
    public string ForegroundExeName { get; init; } = string.Empty;
    public GpuDetectionStatusKind StatusKind { get; init; }
    public int LoadedModuleCount { get; init; }
    public string DetectionModeNote { get; init; } = string.Empty;
    public bool WasAccessDeniedFallback { get; init; }
    public IReadOnlySet<string> DetectedTechIds { get; init; } = new HashSet<string>();

    public static string FormatNote(GpuDetectionStatusKind kind, string exeName, int moduleCount)
    {
        var loc = LocalizationService.Instance;
        return kind switch
        {
            GpuDetectionStatusKind.AntiCheatFallback => loc.Format("GpuDetAntiCheat", exeName),
            GpuDetectionStatusKind.DirectDll => loc.Format("GpuDetDirect", exeName, moduleCount),
            GpuDetectionStatusKind.GameFolder => loc.Format("GpuDetFolder", exeName),
            _ => loc["GpuDetWaiting"]
        };
    }
}

/// <summary>
/// Bộ nhận diện công nghệ GPU an toàn (chỉ đọc danh sách module DLL đã nạp / thư mục cài đặt game
/// và trạng thái driver công khai — tuyệt đối KHÔNG đọc/ghi bộ nhớ hay chèn DLL vào game).
/// Xử lý an toàn mọi ngoại lệ Access Denied trên các game có Anti-Cheat nghiêm ngặt.
/// </summary>
public sealed class GpuTechDetectorService : IDisposable
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TH32CS_SNAPMODULE = 0x00000008;
    private const uint TH32CS_SNAPMODULE32 = 0x00000010;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MODULEENTRY32W
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlblcntUsage;
        public uint ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExePath;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, int th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Module32FirstW(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Module32NextW(IntPtr hSnapshot, ref MODULEENTRY32W lpme);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private static readonly HashSet<string> NonGameProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "dwm", "searchhost", "searchapp", "shellexperiencehost",
        "startmenuexperiencehost", "applicationframehost", "systemsettings",
        "textinputhost", "lockapp", "taskmgr", "winhardwaremonitor", "presentmon",
        "conhost", "windowsterminal", "powershell", "pwsh", "cmd",
        "notepad", "mspaint", "snippingtool", "screenclippinghost", "msiexec",
        "svchost", "runtimebroker", "widgets", "widgetservice", "gamebar",
        "xboxgamebar", "chrome", "msedge", "brave", "firefox", "opera", "vivaldi",
        "code", "devenv", "antigravity", "discord", "slack", "teams", "ms-teams",
        "zalo", "telegram", "spotify", "obs64", "obs32", "nvcontainer",
        "radeonsoftware", "taskhostw", "sihost", "ctfmon", "dllhost", "losslessscaling"
    };

    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    // Cache quét thư mục game để không đọc đĩa lặp lại mỗi 2.5 giây
    private readonly Dictionary<string, HashSet<string>> _gameFolderDllCache = new(StringComparer.OrdinalIgnoreCase);

    // Trạng thái PresentMon phụ trợ (ví dụ phát hiện driver FrameGen Smooth Motion / AFMF)
    private volatile bool _presentMonHasDriverFg;
    private volatile string _presentMonActiveGame = string.Empty;

    public event Action<GpuTechDetectionSnapshot>? SnapshotReady;

    public void UpdatePresentMonHint(bool isGameActive, string activeGameName, string? frameGenSource)
    {
        _presentMonActiveGame = isGameActive ? activeGameName : string.Empty;
        _presentMonHasDriverFg = isGameActive &&
                                 string.Equals(frameGenSource, "FrameGen", StringComparison.OrdinalIgnoreCase);
    }

    public void Start()
    {
        if (_cts != null)
            return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _loopTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(2500));
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    var snap = ScanSafe();
                    SnapshotReady?.Invoke(snap);
                }
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    private GpuTechDetectionSnapshot ScanSafe()
    {
        try
        {
            if (!Win32Helper.TryGetForegroundProcess(out int pid, out string procName))
                return IdleSnapshot();

            if (string.IsNullOrWhiteSpace(procName) || NonGameProcesses.Contains(procName))
                return IdleSnapshot();

            string exeFullPath = TryGetProcessExePath(pid);
            string exeName = !string.IsNullOrWhiteSpace(exeFullPath)
                ? Path.GetFileName(exeFullPath)
                : $"{procName}.exe";

            // Bỏ qua thư mục hệ thống Windows để tránh quét nhầm công cụ hệ điều hành
            if (!string.IsNullOrWhiteSpace(exeFullPath) &&
                exeFullPath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
            {
                return IdleSnapshot();
            }

            // 1. Thử liệt kê các DLL đang được tiến trình nạp (an toàn, chỉ đọc snapshot module)
            bool moduleScanOk = TryEnumerateLoadedModules(pid, out var loadedDlls, out bool accessDenied);

            // 2. Kết hợp quét nhẹ thư mục cài đặt game (nhất là khi Anti-Cheat trả về Access Denied)
            var folderDlls = !string.IsNullOrWhiteSpace(exeFullPath)
                ? GetCachedGameFolderDlls(Path.GetDirectoryName(exeFullPath)!)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Kiểm tra xem tiến trình có thực sự là game/ứng dụng đồ họa 3D hay không
            bool isPresentMonActive = !string.IsNullOrWhiteSpace(_presentMonActiveGame) &&
                                      _presentMonActiveGame.Equals(procName, StringComparison.OrdinalIgnoreCase);

            bool hasGraphicsModule = loadedDlls.Any(IsGraphicsApiDll) || folderDlls.Any(IsGpuTechDll);
            if (!isPresentMonActive && !hasGraphicsModule)
                return IdleSnapshot();

            var allDlls = new HashSet<string>(loadedDlls, StringComparer.OrdinalIgnoreCase);
            foreach (var f in folderDlls)
                allDlls.Add(f);

            var detected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // --- NVIDIA ---
            bool hasDlss = HasAnyDll(allDlls, "nvngx_dlss.dll", "sl.dlss.dll");
            bool hasDlssG = HasAnyDll(allDlls, "nvngx_dlssg.dll", "sl.dlss_g.dll");
            bool hasDlssD = HasAnyDll(allDlls, "nvngx_dlssd.dll", "sl.dlss_d.dll");
            bool hasReflex = HasAnyDll(allDlls, "sl.reflex.dll", "sl.pcl.dll") ||
                             HasPrefixDll(allDlls, "nvreflex") ||
                             HasPrefixDll(allDlls, "nvlowlatency");

            if (hasDlss)
                detected.Add("nv_dlss");
            if (hasDlssG)
                detected.Add("nv_dlss_fg");
            if (hasReflex)
                detected.Add("nv_reflex");

            bool hasSmoothMotion = DetectNvidiaSmoothMotion(hasDlssG);
            if (hasSmoothMotion)
                detected.Add("nv_smooth_motion");

            bool hasDx12OrVulkan = HasAnyDll(allDlls, "d3d12.dll", "d3d12core.dll", "vulkan-1.dll");
            bool hasRayTracingDlls = hasDlssD ||
                                     HasAnyDll(allDlls, "dxr.dll", "dxcompiler.dll") ||
                                     HasPrefixDll(allDlls, "nvrtx") ||
                                     HasPrefixDll(allDlls, "rtxgi") ||
                                     HasPrefixDll(allDlls, "optix");

            if (hasDx12OrVulkan && hasRayTracingDlls)
            {
                detected.Add("nv_rt");
                detected.Add("intel_rt");
            }
            if (hasDlssD)
            {
                // Ray Reconstruction (nvngx_dlssd.dll) thường đi kèm Path Tracing / RT nặng
                detected.Add("nv_pt");
            }

            // --- AMD ---
            bool hasFsr = HasPrefixDll(allDlls, "amd_fidelityfx") ||
                          HasPrefixDll(allDlls, "ffx_fsr");
            bool hasFsrFg = HasPrefixDll(allDlls, "amd_fidelityfx_framegeneration") ||
                            HasPrefixDll(allDlls, "ffx_frameinterpolation") ||
                            HasPrefixDll(allDlls, "ffx_fsr3") ||
                            HasPrefixDll(allDlls, "amd_fidelityfx_dx12") ||
                            HasPrefixDll(allDlls, "amd_fidelityfx_vk");
            bool hasAfmf = DetectAmdDriverAfmf();
            bool hasAmdAntiLag = HasPrefixDll(allDlls, "amd_fidelityfx_antilag") ||
                                 HasPrefixDll(allDlls, "ffx_antilag") ||
                                 DetectAmdDriverAntiLag();

            if (hasFsr)
                detected.Add("amd_fsr");
            if (hasFsrFg || hasAfmf)
                detected.Add("amd_fg_afmf");
            if (hasAmdAntiLag)
                detected.Add("amd_latency");

            // --- Intel ---
            if (HasAnyDll(allDlls, "libxess.dll"))
                detected.Add("intel_xess_sr");
            if (HasAnyDll(allDlls, "libxess_fg.dll"))
                detected.Add("intel_xess_fg");
            if (HasAnyDll(allDlls, "libxell.dll"))
                detected.Add("intel_xell");

            var statusKind = accessDenied
                ? GpuDetectionStatusKind.AntiCheatFallback
                : moduleScanOk
                    ? GpuDetectionStatusKind.DirectDll
                    : GpuDetectionStatusKind.GameFolder;

            return new GpuTechDetectionSnapshot
            {
                IsGameActive = true,
                ForegroundExeName = exeName,
                StatusKind = statusKind,
                LoadedModuleCount = loadedDlls.Count,
                DetectionModeNote = GpuTechDetectionSnapshot.FormatNote(statusKind, exeName, loadedDlls.Count),
                WasAccessDeniedFallback = accessDenied,
                DetectedTechIds = detected
            };
        }
        catch
        {
            // Tuyệt đối không bao giờ để ngoại lệ văng ra khỏi luồng nền
            return IdleSnapshot();
        }
    }

    private static GpuTechDetectionSnapshot IdleSnapshot() => new()
    {
        IsGameActive = false,
        ForegroundExeName = string.Empty,
        StatusKind = GpuDetectionStatusKind.Waiting,
        LoadedModuleCount = 0,
        DetectionModeNote = LocalizationService.Instance["GpuDetWaiting"],
        DetectedTechIds = new HashSet<string>()
    };

    private static string TryGetProcessExePath(int pid)
    {
        IntPtr handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero)
                return string.Empty;

            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            if (QueryFullProcessImageName(handle, 0, sb, ref size))
                return sb.ToString();
        }
        catch { }
        finally
        {
            if (handle != IntPtr.Zero)
                CloseHandle(handle);
        }
        return string.Empty;
    }

    private static bool TryEnumerateLoadedModules(int pid, out HashSet<string> dllNames, out bool accessDenied)
    {
        dllNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        accessDenied = false;

        IntPtr snap = IntPtr.Zero;
        try
        {
            snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
            if (snap == INVALID_HANDLE_VALUE || snap == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                if (err is 5 or 299) // ERROR_ACCESS_DENIED hoặc ERROR_PARTIAL_COPY (Anti-Cheat)
                    accessDenied = true;
                return false;
            }

            var entry = new MODULEENTRY32W { dwSize = (uint)Marshal.SizeOf<MODULEENTRY32W>() };
            if (Module32FirstW(snap, ref entry))
            {
                int guard = 0;
                do
                {
                    if (!string.IsNullOrWhiteSpace(entry.szModule))
                        dllNames.Add(entry.szModule);
                    guard++;
                }
                while (guard < 2000 && Module32NextW(snap, ref entry));
            }
            return dllNames.Count > 0;
        }
        catch
        {
            accessDenied = true;
            return false;
        }
        finally
        {
            if (snap != IntPtr.Zero && snap != INVALID_HANDLE_VALUE)
                CloseHandle(snap);
        }
    }

    private HashSet<string> GetCachedGameFolderDlls(string gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (_gameFolderDllCache.TryGetValue(gameDir, out var cached))
            return cached;

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            // Quét tối đa 2 cấp thư mục (thư mục exe + 1 cấp con / cha phổ biến của Unreal/Unity) với giới hạn an toàn
            var dirsToScan = new List<string> { gameDir };
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(gameDir).Take(16))
                    dirsToScan.Add(sub);
            }
            catch { }

            int scannedFiles = 0;
            foreach (var dir in dirsToScan)
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir, "*.dll"))
                    {
                        scannedFiles++;
                        string name = Path.GetFileName(file);
                        if (IsGpuTechDll(name) || IsGraphicsApiDll(name))
                            found.Add(name);
                        if (scannedFiles > 600)
                            break;
                    }
                }
                catch { }
                if (scannedFiles > 600)
                    break;
            }
        }
        catch { }

        if (_gameFolderDllCache.Count > 32)
            _gameFolderDllCache.Clear();
        _gameFolderDllCache[gameDir] = found;
        return found;
    }

    private bool DetectNvidiaSmoothMotion(bool hasInGameDlssG)
    {
        try
        {
            // Nếu driver đang tạo khung hình mà game không nạp nvngx_dlssg.dll -> dấu hiệu Smooth Motion
            if (_presentMonHasDriverFg && !hasInGameDlssG)
                return true;

            using var key = Registry.CurrentUser.OpenSubKey(@"Software\NVIDIA Corporation\Global\NVTweak");
            if (key != null)
            {
                var val = key.GetValue("SmoothMotion") ?? key.GetValue("EnableSmoothMotion");
                if (val is int i && i == 1)
                    return true;
            }
        }
        catch { }
        return false;
    }

    private bool DetectAmdDriverAfmf()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\AMD\CN");
            if (key != null)
            {
                var val = key.GetValue("AFMF") ?? key.GetValue("FluidMotionFrames");
                if (val is int i && i == 1)
                    return true;
                if (val is string s && (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private bool DetectAmdDriverAntiLag()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\AMD\CN");
            if (key != null)
            {
                var val = key.GetValue("AntiLag") ?? key.GetValue("AntiLagEnabled");
                if (val is int i && i == 1)
                    return true;
                if (val is string s && (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static bool IsGraphicsApiDll(string name) =>
        name.Equals("d3d11.dll", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("d3d12.dll", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("d3d12core.dll", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("vulkan-1.dll", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("d3d9.dll", StringComparison.OrdinalIgnoreCase);

    private static bool IsGpuTechDll(string name) =>
        name.StartsWith("nvngx_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("sl.", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("nvreflex", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("nvrtx", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("amd_fidelityfx", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("ffx_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("libxess", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("libxell", StringComparison.OrdinalIgnoreCase);

    private static bool HasAnyDll(HashSet<string> dlls, params string[] names)
    {
        foreach (var n in names)
        {
            if (dlls.Contains(n))
                return true;
        }
        return false;
    }

    private static bool HasPrefixDll(HashSet<string> dlls, string prefix)
    {
        foreach (var d in dlls)
        {
            if (d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public void Dispose()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            try { _loopTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }
            _cts.Dispose();
            _cts = null;
        }
    }
}
