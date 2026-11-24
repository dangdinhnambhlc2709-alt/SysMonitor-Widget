using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using WinHardwareMonitor.Models;

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
    public GpuVendor? ActiveRenderingVendor { get; init; }
    public string ActiveRenderingGpuId { get; init; } = string.Empty;
    public string ActiveRenderingGpuName { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, GpuTechDetectionSource> DetectedTechSources { get; init; }
        = new Dictionary<string, GpuTechDetectionSource>();

    public static string FormatNote(GpuDetectionStatusKind kind, string exeName, int moduleCount, string activeGpuName)
    {
        var loc = LocalizationService.Instance;
        string baseMsg = kind switch
        {
            GpuDetectionStatusKind.AntiCheatFallback => loc.Format("GpuDetAntiCheat", exeName),
            GpuDetectionStatusKind.DirectDll => loc.Format("GpuDetDirect", exeName, moduleCount),
            GpuDetectionStatusKind.GameFolder => loc.Format("GpuDetFolder", exeName),
            _ => loc["GpuDetWaiting"]
        };

        if (kind != GpuDetectionStatusKind.Waiting && !string.IsNullOrWhiteSpace(activeGpuName))
            return $"{baseMsg} • {loc.Format("GpuRenderingOnFormat", activeGpuName)}";

        return baseMsg;
    }
}

/// <summary>
/// Bộ nhận diện công nghệ GPU 3 lớp siêu an toàn (không chèn DLL, không ghi bộ nhớ):
/// - Lớp 1: Quét thư viện DLL đang nạp trong tiến trình game (Toolhelp32 + EnumProcessModulesEx) gồm NvPresent64.dll (Smooth Motion).
/// - Lớp 2: Tự leo ngược tối đa 3 cấp đến Thư mục gốc game (Wuthering Waves / Unreal / Unity...) rồi quét sâu 8 cấp có bộ nhớ đệm theo .exe.
/// - Lớp 3: Đối chiếu tỷ lệ FPS hiển thị / FPS gốc từ PresentMon (> 1.45x duy trì >= 3 giây, không Lossless Scaling) + GPU tải chính (Hybrid Optimus).
/// </summary>
public sealed class GpuTechDetectorService : IDisposable
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint TH32CS_SNAPMODULE = 0x00000008;
    private const uint TH32CS_SNAPMODULE32 = 0x00000010;
    private const uint LIST_MODULES_ALL = 0x03;
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

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EnumProcessModulesEx(
        IntPtr hProcess,
        [Out] IntPtr[] lphModule,
        uint cb,
        out uint lpcbNeeded,
        uint dwFilterFlag);

    [DllImport("psapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetModuleFileNameExW(
        IntPtr hProcess,
        IntPtr hModule,
        StringBuilder lpFilename,
        uint nSize);

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

    private static readonly HashSet<string> UnsafeClimbRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        "program files", "program files (x86)", "common", "steamapps", "steam",
        "epic games", "games", "xboxgames", "windows", "users", "appdata",
        "riot games", "ea games", "ubisoft game launcher", "gog galaxy"
    };

    private static readonly HashSet<string> BinarySubfolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "win64", "win32", "binaries", "client", "x64", "x86", "bin", "bin64", "bin32", "shipping", "release"
    };

    private static readonly HashSet<string> GameRootMarkers = new(StringComparer.OrdinalIgnoreCase)
    {
        "engine", "plugins", "content", "client"
    };

    private static readonly HashSet<string> PriorityScanDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "plugins", "engine", "binaries", "thirdparty", "client", "win64", "redist", "nvngx", "streamline"
    };

    private readonly object _stateLock = new();
    private CancellationTokenSource? _cts;
    private Task? _loopTask;

    // Cache quét thư mục gốc game theo đúng đường dẫn .exe (mỗi game chỉ quét đĩa đúng 1 lần trong phiên)
    private readonly Dictionary<string, HashSet<string>> _exeFolderDllCache = new(StringComparer.OrdinalIgnoreCase);

    // Dữ liệu Lớp 3 (PresentMon) & trạng thái tải GPU thực tế (Hybrid Laptop 2 GPU)
    private string _presentMonActiveGame = string.Empty;
    private bool _presentMonSustainedDriverFg;
    private string _sustainedFgGame = string.Empty;
    private readonly Stopwatch _sustainedFgWatch = new();
    private List<GpuRuntimeActivity> _gpuActivities = new();
    private List<DetectedGpuInfo> _knownGpus = new();

    public event Action<GpuTechDetectionSnapshot>? SnapshotReady;

    public void SetKnownGpus(IReadOnlyList<DetectedGpuInfo> gpus)
    {
        lock (_stateLock)
        {
            _knownGpus = gpus.ToList();
        }
    }

    public void UpdateGpuActivity(IReadOnlyList<GpuRuntimeActivity> activities)
    {
        lock (_stateLock)
        {
            _gpuActivities = activities.ToList();
        }
    }

    /// <summary>
    /// Nhận dữ liệu khung hình thời gian thực từ PresentMon để kiểm tra Lớp 3:
    /// Displayed FPS > 1.45x Base FPS duy trì ổn định >= 3 giây liên tiếp và không chạy Lossless Scaling.
    /// </summary>
    public void UpdatePresentMonMetrics(FpsSnapshot snap)
    {
        lock (_stateLock)
        {
            _presentMonActiveGame = snap.IsGameActive ? snap.ActiveGameName : string.Empty;

            bool highRatio = snap.IsGameActive &&
                             !snap.IsLosslessScalingActive &&
                             snap.BaseFps.HasValue && snap.BaseFps.Value >= 15.0 &&
                             snap.RawDisplayedFps.HasValue &&
                             snap.RawDisplayedFps.Value > snap.BaseFps.Value * 1.45;

            if (!snap.IsGameActive ||
                !string.Equals(_sustainedFgGame, snap.ActiveGameName, StringComparison.OrdinalIgnoreCase))
            {
                _sustainedFgGame = snap.IsGameActive ? snap.ActiveGameName : string.Empty;
                _sustainedFgWatch.Reset();
                _presentMonSustainedDriverFg = false;
            }

            if (highRatio)
            {
                if (!_sustainedFgWatch.IsRunning)
                    _sustainedFgWatch.Restart();

                if (_sustainedFgWatch.Elapsed.TotalSeconds >= 3.0)
                    _presentMonSustainedDriverFg = true;
            }
            else
            {
                // Nếu tỷ lệ tụt xuống dưới ngưỡng an toàn (1.35x) hoặc bật Lossless Scaling -> reset
                bool stillBorderline = snap.IsGameActive &&
                                       !snap.IsLosslessScalingActive &&
                                       snap.BaseFps.HasValue && snap.BaseFps.Value >= 15.0 &&
                                       snap.RawDisplayedFps.HasValue &&
                                       snap.RawDisplayedFps.Value > snap.BaseFps.Value * 1.35;
                if (!stillBorderline)
                {
                    _sustainedFgWatch.Reset();
                    _presentMonSustainedDriverFg = false;
                }
            }
        }
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

            // Bỏ qua thư mục hệ thống Windows
            if (!string.IsNullOrWhiteSpace(exeFullPath) &&
                exeFullPath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase))
            {
                return IdleSnapshot();
            }

            // LỚP 1: Quét thư viện DLL đang nạp trong bộ nhớ tiến trình game
            bool moduleScanOk = TryEnumerateLoadedModules(pid, out var loadedDlls, out bool accessDenied);

            // LỚP 2: Quét từ Thư mục gốc game (dự phòng an toàn cho Anti-Cheat như Wuthering Waves, có cache theo .exe)
            var folderDlls = !string.IsNullOrWhiteSpace(exeFullPath)
                ? GetCachedGameRootDlls(exeFullPath)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string pmGame;
            bool pmSustainedFg;
            List<GpuRuntimeActivity> activitiesCopy;
            List<DetectedGpuInfo> knownGpusCopy;
            lock (_stateLock)
            {
                pmGame = _presentMonActiveGame;
                pmSustainedFg = _presentMonSustainedDriverFg;
                activitiesCopy = _gpuActivities.ToList();
                knownGpusCopy = _knownGpus.ToList();
            }

            bool isPresentMonActive = !string.IsNullOrWhiteSpace(pmGame) &&
                                      pmGame.Equals(procName, StringComparison.OrdinalIgnoreCase);

            bool hasGraphicsModule = loadedDlls.Any(IsGraphicsApiDll) || folderDlls.Any(IsGpuTechDll);
            if (!isPresentMonActive && !hasGraphicsModule)
                return IdleSnapshot();

            // Xác định GPU đang thực sự kết xuất game (xử lý chuẩn Laptop Hybrid 2 GPU AMD/Intel iGPU + NVIDIA dGPU)
            var (activeVendor, activeGpuId, activeGpuName) = ResolveActiveRenderingGpu(loadedDlls, activitiesCopy, knownGpusCopy);

            var detected = new Dictionary<string, GpuTechDetectionSource>(StringComparer.OrdinalIgnoreCase);

            void Mark(string techId, GpuTechDetectionSource source)
            {
                if (!detected.TryGetValue(techId, out var existing) || source < existing)
                    detected[techId] = source;
            }

            // Phân tích riêng Lớp 1 (MemoryDll) ưu tiên cao nhất, sau đó bổ sung Lớp 2 (GameFolder)
            EvaluateDllSet(loadedDlls, GpuTechDetectionSource.MemoryDll, Mark);
            EvaluateDllSet(folderDlls, GpuTechDetectionSource.GameFolder, Mark);

            // Bổ sung đọc khóa Registry công khai cho Smooth Motion / AFMF / Anti-Lag nếu có
            if (DetectNvidiaRegistrySmoothMotion() && activeVendor is null or GpuVendor.Nvidia)
                Mark("nv_smooth_motion", GpuTechDetectionSource.MemoryDll);
            if (DetectAmdDriverAfmf() && activeVendor is null or GpuVendor.Amd)
                Mark("amd_fg_afmf", GpuTechDetectionSource.MemoryDll);
            if (DetectAmdDriverAntiLag() && activeVendor is null or GpuVendor.Amd)
                Mark("amd_latency", GpuTechDetectionSource.MemoryDll);

            // LỚP 3: Đối chiếu PresentMon (Displayed FPS > 1.45x Base FPS duy trì >= 3s, không Lossless Scaling)
            if (isPresentMonActive && pmSustainedFg)
            {
                bool hasNvidiaGpu = knownGpusCopy.Any(g => g.Vendor == GpuVendor.Nvidia);
                bool hasAmdGpu = knownGpusCopy.Any(g => g.Vendor == GpuVendor.Amd);

                if ((activeVendor == GpuVendor.Nvidia || (activeVendor == null && hasNvidiaGpu)) &&
                    !detected.ContainsKey("nv_dlss_fg"))
                {
                    Mark("nv_smooth_motion", GpuTechDetectionSource.PresentMon);
                }
                else if ((activeVendor == GpuVendor.Amd || (activeVendor == null && hasAmdGpu)) &&
                         !detected.ContainsKey("amd_fg_afmf"))
                {
                    Mark("amd_fg_afmf", GpuTechDetectionSource.PresentMon);
                }
            }

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
                DetectionModeNote = GpuTechDetectionSnapshot.FormatNote(statusKind, exeName, loadedDlls.Count, activeGpuName),
                WasAccessDeniedFallback = accessDenied,
                ActiveRenderingVendor = activeVendor,
                ActiveRenderingGpuId = activeGpuId,
                ActiveRenderingGpuName = activeGpuName,
                DetectedTechSources = detected
            };
        }
        catch
        {
            return IdleSnapshot();
        }
    }

    private static void EvaluateDllSet(
        HashSet<string> dlls,
        GpuTechDetectionSource source,
        Action<string, GpuTechDetectionSource> mark)
    {
        if (dlls.Count == 0)
            return;

        // --- NVIDIA ---
        if (HasAnyDll(dlls, "nvpresent64.dll", "nvpresent.dll"))
            mark("nv_smooth_motion", source);

        if (HasAnyDll(dlls, "nvngx_dlss.dll", "sl.dlss.dll"))
            mark("nv_dlss", source);

        if (HasAnyDll(dlls, "nvngx_dlssg.dll", "sl.dlss_g.dll"))
            mark("nv_dlss_fg", source);

        bool hasDlssD = HasAnyDll(dlls, "nvngx_dlssd.dll", "sl.dlss_d.dll");
        if (hasDlssD)
            mark("nv_pt", source);

        if (HasAnyDll(dlls, "sl.reflex.dll", "sl.pcl.dll") ||
            HasPrefixDll(dlls, "nvreflex") ||
            HasPrefixDll(dlls, "nvlowlatency"))
        {
            mark("nv_reflex", source);
        }

        // Ray Tracing
        bool hasDx12OrVulkan = HasAnyDll(dlls, "d3d12.dll", "d3d12core.dll", "vulkan-1.dll") || source == GpuTechDetectionSource.GameFolder;
        bool hasRayTracingDlls = hasDlssD ||
                                 HasAnyDll(dlls, "dxr.dll", "dxcompiler.dll") ||
                                 HasPrefixDll(dlls, "nvrtx") ||
                                 HasPrefixDll(dlls, "rtxgi") ||
                                 HasPrefixDll(dlls, "optix");

        if (hasDx12OrVulkan && hasRayTracingDlls)
        {
            mark("nv_rt", source);
            mark("intel_rt", source);
        }

        // --- AMD ---
        if (HasPrefixDll(dlls, "amd_fidelityfx") || HasPrefixDll(dlls, "ffx_fsr"))
            mark("amd_fsr", source);

        if (HasPrefixDll(dlls, "amd_fidelityfx_framegeneration") ||
            HasPrefixDll(dlls, "ffx_frameinterpolation") ||
            HasPrefixDll(dlls, "ffx_fsr3"))
        {
            mark("amd_fg_afmf", source);
        }

        if (HasPrefixDll(dlls, "amd_fidelityfx_antilag") || HasPrefixDll(dlls, "ffx_antilag"))
            mark("amd_latency", source);

        // --- Intel ---
        if (HasAnyDll(dlls, "libxess.dll"))
            mark("intel_xess_sr", source);
        if (HasAnyDll(dlls, "libxess_fg.dll"))
            mark("intel_xess_fg", source);
        if (HasAnyDll(dlls, "libxell.dll"))
            mark("intel_xell", source);
    }

    /// <summary>
    /// Xác định GPU đang thực sự kết xuất game trên máy 2 GPU (Hybrid / Optimus) dựa trên
    /// DLL trình điều khiển đã nạp trong tiến trình + mức tải 3D & VRAM đang sử dụng thực tế.
    /// </summary>
    private static (GpuVendor? Vendor, string GpuId, string GpuName) ResolveActiveRenderingGpu(
        HashSet<string> loadedDlls,
        List<GpuRuntimeActivity> activities,
        List<DetectedGpuInfo> knownGpus)
    {
        // 1. Ưu tiên kiểm tra tải 3D & VRAM thực tế giữa các GPU vật lý
        if (activities.Count > 0)
        {
            var topActive = activities
                .OrderByDescending(a => a.LoadPercent + (a.IsDiscrete && a.VramUsedMb > 450 ? 25.0 : 0.0) + (a.VramUsedMb > 700 ? 15.0 : 0.0))
                .First();

            if (topActive.LoadPercent >= 18.0 || (topActive.IsDiscrete && topActive.VramUsedMb >= 600.0))
                return (topActive.Vendor, topActive.GpuId, topActive.Name);
        }

        // 2. Kiểm tra thư viện trình điều khiển đồ họa đang nạp trong tiến trình game
        bool hasNvidiaDriver = HasAnyDll(loadedDlls, "nvwgf2umx.dll", "nvoglv64.dll", "nvd3dumx.dll", "nvldumdx.dll", "nvpresent64.dll", "nvpresent.dll");
        bool hasAmdDriver = HasAnyDll(loadedDlls, "amdxc64.dll", "atidxx64.dll", "atio6axx.dll", "amd_ags_x64.dll");
        bool hasIntelDriver = HasAnyDll(loadedDlls, "igd12um64xel.dll", "igd10um64xe.dll", "igc64.dll");

        if (hasNvidiaDriver)
        {
            var nv = knownGpus.FirstOrDefault(g => g.Vendor == GpuVendor.Nvidia);
            return (GpuVendor.Nvidia, nv?.Id ?? string.Empty, nv?.Name ?? "NVIDIA GPU");
        }
        if (hasAmdDriver)
        {
            var amd = knownGpus.FirstOrDefault(g => g.Vendor == GpuVendor.Amd && g.IsDiscrete)
                      ?? knownGpus.FirstOrDefault(g => g.Vendor == GpuVendor.Amd);
            return (GpuVendor.Amd, amd?.Id ?? string.Empty, amd?.Name ?? "AMD GPU");
        }
        if (hasIntelDriver)
        {
            var intel = knownGpus.FirstOrDefault(g => g.Vendor == GpuVendor.Intel);
            return (GpuVendor.Intel, intel?.Id ?? string.Empty, intel?.Name ?? "Intel GPU");
        }

        // 3. Fallback ưu tiên card rời nếu máy có nhiều GPU
        var discrete = knownGpus.FirstOrDefault(g => g.IsDiscrete) ?? knownGpus.FirstOrDefault();
        return discrete != null
            ? (discrete.Vendor, discrete.Id, discrete.Name)
            : (null, string.Empty, string.Empty);
    }

    private static GpuTechDetectionSnapshot IdleSnapshot() => new()
    {
        IsGameActive = false,
        ForegroundExeName = string.Empty,
        StatusKind = GpuDetectionStatusKind.Waiting,
        LoadedModuleCount = 0,
        DetectionModeNote = LocalizationService.Instance["GpuDetWaiting"],
        DetectedTechSources = new Dictionary<string, GpuTechDetectionSource>()
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

    /// <summary>
    /// Lớp 1: Liệt kê module DLL đang nạp bằng CreateToolhelp32Snapshot + dự phòng EnumProcessModulesEx.
    /// </summary>
    private static bool TryEnumerateLoadedModules(int pid, out HashSet<string> dllNames, out bool accessDenied)
    {
        dllNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        accessDenied = false;

        // Cách 1: CreateToolhelp32Snapshot
        IntPtr snap = IntPtr.Zero;
        try
        {
            snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
            if (snap == INVALID_HANDLE_VALUE || snap == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                if (err is 5 or 299)
                    accessDenied = true;
            }
            else
            {
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
                    while (guard < 2500 && Module32NextW(snap, ref entry));
                }
            }
        }
        catch
        {
            accessDenied = true;
        }
        finally
        {
            if (snap != IntPtr.Zero && snap != INVALID_HANDLE_VALUE)
                CloseHandle(snap);
        }

        if (dllNames.Count > 0)
            return true;

        // Cách 2: EnumProcessModulesEx (PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ)
        IntPtr hProc = IntPtr.Zero;
        try
        {
            hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ, false, pid);
            if (hProc == IntPtr.Zero)
            {
                if (Marshal.GetLastWin32Error() == 5)
                    accessDenied = true;
                return false;
            }

            var modules = new IntPtr[1024];
            if (EnumProcessModulesEx(hProc, modules, (uint)(modules.Length * IntPtr.Size), out uint bytesNeeded, LIST_MODULES_ALL))
            {
                int count = (int)Math.Min(modules.Length, bytesNeeded / IntPtr.Size);
                var sb = new StringBuilder(512);
                for (int i = 0; i < count; i++)
                {
                    sb.Clear();
                    if (GetModuleFileNameExW(hProc, modules[i], sb, (uint)sb.Capacity) > 0)
                    {
                        string fileName = Path.GetFileName(sb.ToString());
                        if (!string.IsNullOrWhiteSpace(fileName))
                            dllNames.Add(fileName);
                    }
                }
            }
            else if (Marshal.GetLastWin32Error() is 5 or 299)
            {
                accessDenied = true;
            }
        }
        catch
        {
            accessDenied = true;
        }
        finally
        {
            if (hProc != IntPtr.Zero)
                CloseHandle(hProc);
        }

        return dllNames.Count > 0;
    }

    /// <summary>
    /// Lớp 2: Tự leo ngược tối đa 3 cấp tới Thư mục gốc game rồi quét đệ quy tối đa 8 cấp
    /// (ưu tiên Plugins, Engine, Binaries, ThirdParty) và lưu bộ nhớ đệm vĩnh viễn theo đường dẫn .exe.
    /// </summary>
    private HashSet<string> GetCachedGameRootDlls(string exeFullPath)
    {
        if (string.IsNullOrWhiteSpace(exeFullPath))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        lock (_exeFolderDllCache)
        {
            if (_exeFolderDllCache.TryGetValue(exeFullPath, out var cached))
                return cached;
        }

        string? exeDir = Path.GetDirectoryName(exeFullPath);
        if (string.IsNullOrWhiteSpace(exeDir) || !Directory.Exists(exeDir))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string gameRoot = ResolveSafeGameRoot(exeDir);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Quét theo chiều rộng có ưu tiên thư mục Plugins / Engine / Binaries / ThirdParty (tối đa 8 cấp)
            var queue = new Queue<(string Dir, int Depth)>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            queue.Enqueue((exeDir, 0));
            visited.Add(exeDir);

            if (!string.Equals(gameRoot, exeDir, StringComparison.OrdinalIgnoreCase))
            {
                queue.Enqueue((gameRoot, 0));
                visited.Add(gameRoot);
            }

            int scannedFiles = 0;
            int scannedDirs = 0;
            var sw = Stopwatch.StartNew();

            while (queue.Count > 0 && scannedFiles < 6000 && scannedDirs < 600 && sw.ElapsedMilliseconds < 450)
            {
                var (dir, depth) = queue.Dequeue();
                scannedDirs++;

                try
                {
                    foreach (var file in Directory.EnumerateFiles(dir, "*.dll"))
                    {
                        scannedFiles++;
                        string name = Path.GetFileName(file);
                        if (IsGpuTechDll(name) || IsGraphicsApiDll(name))
                            found.Add(name);
                        if (scannedFiles >= 6000)
                            break;
                    }
                }
                catch { }

                if (depth >= 8)
                    continue;

                try
                {
                    var subDirs = Directory.EnumerateDirectories(dir)
                        .OrderByDescending(d => PriorityScanDirs.Contains(Path.GetFileName(d)))
                        .Take(48);

                    foreach (var sub in subDirs)
                    {
                        if (visited.Add(sub))
                            queue.Enqueue((sub, depth + 1));
                    }
                }
                catch { }
            }
        }
        catch { }

        lock (_exeFolderDllCache)
        {
            if (_exeFolderDllCache.Count > 64)
                _exeFolderDllCache.Clear();
            _exeFolderDllCache[exeFullPath] = found;
        }

        return found;
    }

    private static string ResolveSafeGameRoot(string exeDir)
    {
        string bestRoot = exeDir;
        var current = new DirectoryInfo(exeDir);

        for (int step = 0; step < 3; step++)
        {
            var parent = current.Parent;
            if (parent == null || parent.Parent == null) // Dừng an toàn trước ổ gốc (C:\, D:\)
                break;

            string parentName = parent.Name;
            if (UnsafeClimbRoots.Contains(parentName))
                break;

            // Dừng nếu cha là thư mục chứa nhiều game (VD: ...\SteamApps\common)
            if (parent.FullName.EndsWith(@"steamapps\common", StringComparison.OrdinalIgnoreCase))
                break;

            bool currentLooksLikeBin = BinarySubfolderNames.Contains(current.Name);
            bool parentHasMarkers = HasGameRootMarkerSubdir(parent.FullName);

            if (currentLooksLikeBin || parentHasMarkers)
            {
                bestRoot = parent.FullName;
                current = parent;
                if (parentHasMarkers && !BinarySubfolderNames.Contains(parent.Name))
                {
                    // Kiểm tra thêm 1 cấp nếu thư mục hiện tại là "Client" (như Wuthering Waves Game\Client)
                    if (!parent.Name.Equals("Client", StringComparison.OrdinalIgnoreCase))
                        break;
                }
            }
            else
            {
                break;
            }
        }

        return bestRoot;
    }

    private static bool HasGameRootMarkerSubdir(string dir)
    {
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(dir).Take(24))
            {
                if (GameRootMarkers.Contains(Path.GetFileName(sub)))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static bool DetectNvidiaRegistrySmoothMotion()
    {
        try
        {
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

    private static bool DetectAmdDriverAfmf()
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

    private static bool DetectAmdDriverAntiLag()
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
        name.Equals("nvpresent64.dll", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("nvpresent.dll", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("amd_ags_x64.dll", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("dxr.dll", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("dxcompiler.dll", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("nvngx_", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("sl.", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("nvreflex", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("nvlowlatency", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("nvrtx", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("rtxgi", StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith("optix", StringComparison.OrdinalIgnoreCase) ||
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
