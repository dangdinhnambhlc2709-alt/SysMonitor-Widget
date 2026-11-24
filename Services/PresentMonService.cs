using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WinHardwareMonitor.Services;

public readonly record struct FpsGraphPoint(double BaseFps, double? FrameGenFps);

public enum PresentMonStatusKind
{
    Missing,
    Stopped,
    Error,
    Waiting,
    Tracking
}

public sealed class FpsSnapshot
{
    public bool IsPresentMonAvailable { get; init; }
    public bool IsPresentMonRunning { get; init; }
    public PresentMonStatusKind StatusKind { get; init; }
    public string StatusErrorDetail { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public bool IsGameActive { get; init; }
    public string ActiveGameName { get; init; } = string.Empty;
    public double? BaseFps { get; init; }
    public double? FrameGenFps { get; init; }
    public string? FrameGenSource { get; init; }
    public double? AvgFps { get; init; }
    public double? OnePercentLowFps { get; init; }
    public double? PointOnePercentLowFps { get; init; }
    public IReadOnlyList<FpsGraphPoint> GraphHistory { get; init; } = Array.Empty<FpsGraphPoint>();
}

/// <summary>
/// Thu thập dữ liệu khung hình thời gian thực bằng Intel PresentMon 2.x (ETW mức hệ điều hành,
/// hoàn toàn không chèn DLL vào tiến trình game — an toàn tuyệt đối với Anti-Cheat).
/// </summary>
public sealed class PresentMonService : IDisposable
{
    private const double SlidingWindowSeconds = 60.0;
    private const double RecentFpsWindowSeconds = 1.0;
    private const int MaxGraphPoints = 60;
    private const string EmbeddedResourceName = "WinHardwareMonitor.tools.PresentMon.exe";

    private static readonly HashSet<string> ExcludedProcesses = new(StringComparer.OrdinalIgnoreCase)
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
        "radeonsoftware", "nvidia overlay", "nvidia app", "amdsoftware",
        "taskhostw", "sihost", "ctfmon", "dllhost", "mmdevapi"
    };

    private readonly record struct FrameSample(
        double WallTimeSec,
        double BaseFrameTimeMs,
        double? DisplayIntervalMs,
        bool IsDisplayed,
        bool IsGenerated);

    private sealed class ProcessFrameBuffer
    {
        public string ProcessName { get; set; } = string.Empty;
        public double LastSeenWallSec { get; set; }
        public Queue<FrameSample> Samples { get; } = new();
    }

    private readonly object _lock = new();
    private readonly Dictionary<int, ProcessFrameBuffer> _buffers = new();
    private readonly Queue<FpsGraphPoint> _graphHistory = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private Process? _presentMonProcess;
    private CancellationTokenSource? _cts;
    private Task? _readerTask;
    private Task? _publishTask;

    private int _activeGamePid = -1;
    private string _activeGameName = string.Empty;
    private double _manualResetWallSec;
    private string _presentMonPath = string.Empty;
    private string _lastError = string.Empty;
    private bool _isRunning;

    // Chỉ số cột CSV động từ header thực tế của PresentMon 2.x
    private int _colApp = -1;
    private int _colPid = -1;
    private int _colFrameTime = -1;
    private int _colMsBetweenPresents = -1;
    private int _colMsBetweenDisplayChange = -1;
    private int _colDisplayedTime = -1;
    private int _colMsUntilDisplayed = -1;
    private int _colFrameType = -1;
    private int _colDropped = -1;
    private bool _headerParsed;

    public string PresentMonPath => _presentMonPath;
    public bool IsPresentMonAvailable => !string.IsNullOrWhiteSpace(_presentMonPath) && File.Exists(_presentMonPath);
    public bool IsRunning => _isRunning;

    public event Action<FpsSnapshot>? SnapshotReady;

    public void Start()
    {
        if (_cts != null)
            return;

        _presentMonPath = ResolveOrExtractPresentMon();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        if (IsPresentMonAvailable)
        {
            StartPresentMonProcess(token);
        }
        else
        {
            _lastError = string.Empty;
        }

        _publishTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    var snapshot = BuildSnapshot();
                    SnapshotReady?.Invoke(snapshot);
                }
            }
            catch (OperationCanceledException) { }
        }, token);
    }

    public void ResetStats()
    {
        lock (_lock)
        {
            _manualResetWallSec = _clock.Elapsed.TotalSeconds;
            foreach (var buf in _buffers.Values)
                buf.Samples.Clear();
            _graphHistory.Clear();
        }
    }

    public async Task<(bool Success, string Message)> DownloadOfficialPresentMonAsync()
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WinHardwareMonitor/1.0");
            http.Timeout = TimeSpan.FromSeconds(45);

            string json = await http.GetStringAsync("https://api.github.com/repos/GameTechDev/PresentMon/releases?per_page=8");
            using var doc = JsonDocument.Parse(json);

            string? downloadUrl = null;
            string? assetName = null;

            foreach (var release in doc.RootElement.EnumerateArray())
            {
                if (!release.TryGetProperty("assets", out var assets))
                    continue;

                foreach (var asset in assets.EnumerateArray())
                {
                    string name = asset.GetProperty("name").GetString() ?? string.Empty;
                    if (name.StartsWith("PresentMon-2.", StringComparison.OrdinalIgnoreCase) &&
                        name.EndsWith("-x64.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        assetName = name;
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }
                if (downloadUrl != null) break;
            }

            if (string.IsNullOrWhiteSpace(downloadUrl))
                return (false, LocalizationService.Instance["PmErrNotFound"]);

            string targetDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinHardwareMonitor", "tools");
            Directory.CreateDirectory(targetDir);
            string targetPath = Path.Combine(targetDir, "PresentMon.exe");

            StopPresentMonProcess();

            byte[] data = await http.GetByteArrayAsync(downloadUrl);
            await File.WriteAllBytesAsync(targetPath, data);

            _presentMonPath = targetPath;
            _lastError = string.Empty;

            if (_cts != null && !_cts.IsCancellationRequested)
                StartPresentMonProcess(_cts.Token);

            return (true, LocalizationService.Instance.Format("PmDownloadSuccess", assetName ?? "PresentMon.exe"));
        }
        catch (Exception ex)
        {
            return (false, LocalizationService.Instance.Format("PmDownloadFailed", ex.Message));
        }
    }

    private string ResolveOrExtractPresentMon()
    {
        string baseDir = AppContext.BaseDirectory;
        string localToolsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinHardwareMonitor", "tools");

        string[] candidates =
        {
            Path.Combine(baseDir, "tools", "PresentMon.exe"),
            Path.Combine(baseDir, "PresentMon.exe"),
            Path.Combine(localToolsDir, "PresentMon.exe")
        };

        foreach (var path in candidates)
        {
            if (File.Exists(path) && new FileInfo(path).Length > 100_000)
                return path;
        }

        // Tự động giải nén PresentMon.exe chính chủ được đóng gói sẵn bên trong ứng dụng
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream(EmbeddedResourceName);
            if (stream != null)
            {
                Directory.CreateDirectory(localToolsDir);
                string extractPath = Path.Combine(localToolsDir, "PresentMon.exe");
                using (var fs = File.Create(extractPath))
                {
                    stream.CopyTo(fs);
                }
                if (File.Exists(extractPath))
                    return extractPath;
            }
        }
        catch (Exception ex)
        {
            _lastError = LocalizationService.Instance.Format("PmExtractFailed", ex.Message);
        }

        return string.Empty;
    }

    private void StartPresentMonProcess(CancellationToken token)
    {
        StopPresentMonProcess();
        _headerParsed = false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _presentMonPath,
                Arguments = "--output_stdout --no_console_stats --v2_metrics --session_name WinHardwareMonitor_PM --stop_existing_session",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(_presentMonPath) ?? AppContext.BaseDirectory
            };

            _presentMonProcess = Process.Start(psi);
            if (_presentMonProcess == null)
            {
                _lastError = LocalizationService.Instance["PmStartFailed"];
                _isRunning = false;
                return;
            }

            _isRunning = true;
            _lastError = string.Empty;

            var proc = _presentMonProcess;
            _readerTask = Task.Run(async () =>
            {
                try
                {
                    // Đọc stderr song song để không bao giờ bị nghẽn pipe
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            while (!token.IsCancellationRequested && !proc.HasExited)
                            {
                                string? errLine = await proc.StandardError.ReadLineAsync(token).ConfigureAwait(false);
                                if (errLine == null) break;
                                if (errLine.Contains("error", StringComparison.OrdinalIgnoreCase))
                                    _lastError = errLine.Trim();
                            }
                        }
                        catch { }
                    }, token);

                    while (!token.IsCancellationRequested)
                    {
                        string? line = await proc.StandardOutput.ReadLineAsync(token).ConfigureAwait(false);
                        if (line == null)
                            break;

                        ProcessCsvLine(line);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                }
                finally
                {
                    _isRunning = false;
                }
            }, token);
        }
        catch (Exception ex)
        {
            _isRunning = false;
            _lastError = ex.Message;
        }
    }

    private void ProcessCsvLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        if (!_headerParsed)
        {
            if (line.Contains("Application", StringComparison.OrdinalIgnoreCase) &&
                line.Contains("ProcessID", StringComparison.OrdinalIgnoreCase))
            {
                ParseHeader(line);
            }
            return;
        }

        string[] parts = line.Split(',');
        if (_colPid < 0 || _colPid >= parts.Length)
            return;

        if (!int.TryParse(parts[_colPid].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) || pid <= 0)
            return;

        string appName = (_colApp >= 0 && _colApp < parts.Length) ? parts[_colApp].Trim() : string.Empty;
        string cleanProcName = Path.GetFileNameWithoutExtension(appName);

        // Bỏ qua sớm tiến trình hệ thống để giảm tải CPU/RAM
        if (!string.IsNullOrWhiteSpace(cleanProcName) &&
            !cleanProcName.Equals("LosslessScaling", StringComparison.OrdinalIgnoreCase) &&
            ExcludedProcesses.Contains(cleanProcName))
        {
            return;
        }

        double? baseFt = TryParseDouble(parts, _colMsBetweenPresents)
                         ?? TryParseDouble(parts, _colFrameTime);

        if (!baseFt.HasValue || baseFt.Value <= 0.05 || baseFt.Value > 5000.0)
            return;

        double? displayInterval = TryParseDouble(parts, _colMsBetweenDisplayChange)
                                  ?? TryParseDouble(parts, _colDisplayedTime);

        bool isDisplayed = true;
        if (_colDropped >= 0 && _colDropped < parts.Length)
        {
            string droppedStr = parts[_colDropped].Trim();
            if (droppedStr == "1" || droppedStr.Equals("true", StringComparison.OrdinalIgnoreCase))
                isDisplayed = false;
        }
        else if (_colMsUntilDisplayed >= 0 && _colMsUntilDisplayed < parts.Length)
        {
            string val = parts[_colMsUntilDisplayed].Trim();
            if (val.Equals("NA", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(val))
                isDisplayed = false;
        }

        bool isGenerated = false;
        if (_colFrameType >= 0 && _colFrameType < parts.Length)
        {
            string ftStr = parts[_colFrameType].Trim();
            if (!string.IsNullOrEmpty(ftStr) &&
                !ftStr.Equals("Application", StringComparison.OrdinalIgnoreCase) &&
                !ftStr.Equals("NotSet", StringComparison.OrdinalIgnoreCase) &&
                !ftStr.Equals("NA", StringComparison.OrdinalIgnoreCase))
            {
                isGenerated = true;
            }
        }

        double nowSec = _clock.Elapsed.TotalSeconds;
        var sample = new FrameSample(nowSec, baseFt.Value, displayInterval, isDisplayed, isGenerated);

        lock (_lock)
        {
            if (!_buffers.TryGetValue(pid, out var buf))
            {
                buf = new ProcessFrameBuffer { ProcessName = cleanProcName };
                _buffers[pid] = buf;
            }
            else if (string.IsNullOrEmpty(buf.ProcessName) && !string.IsNullOrEmpty(cleanProcName))
            {
                buf.ProcessName = cleanProcName;
            }

            buf.LastSeenWallSec = nowSec;
            buf.Samples.Enqueue(sample);

            double cutoff = Math.Max(_manualResetWallSec, nowSec - SlidingWindowSeconds);
            while (buf.Samples.Count > 0 && buf.Samples.Peek().WallTimeSec < cutoff)
                buf.Samples.Dequeue();
        }
    }

    private void ParseHeader(string headerLine)
    {
        var cols = headerLine.Split(',');
        _colApp = FindCol(cols, "Application");
        _colPid = FindCol(cols, "ProcessID");
        _colFrameTime = FindCol(cols, "FrameTime", "MsBetweenPresents");
        _colMsBetweenPresents = FindCol(cols, "MsBetweenPresents");
        _colMsBetweenDisplayChange = FindCol(cols, "MsBetweenDisplayChange");
        _colDisplayedTime = FindCol(cols, "DisplayedTime", "MsBetweenDisplayChange");
        _colMsUntilDisplayed = FindCol(cols, "MsUntilDisplayed", "DisplayLatency");
        _colFrameType = FindCol(cols, "FrameType");
        _colDropped = FindCol(cols, "Dropped");
        _headerParsed = _colPid >= 0 && (_colFrameTime >= 0 || _colMsBetweenPresents >= 0);
    }

    private static int FindCol(string[] cols, params string[] names)
    {
        foreach (var target in names)
        {
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i].Trim().Equals(target, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
        }
        return -1;
    }

    private static double? TryParseDouble(string[] parts, int idx)
    {
        if (idx < 0 || idx >= parts.Length)
            return null;
        string s = parts[idx].Trim();
        if (string.IsNullOrEmpty(s) || s.Equals("NA", StringComparison.OrdinalIgnoreCase))
            return null;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) &&
               !double.IsNaN(v) && !double.IsInfinity(v)
            ? v
            : null;
    }

    private FpsSnapshot BuildSnapshot()
    {
        double nowSec = _clock.Elapsed.TotalSeconds;
        bool hasFg = Win32Helper.TryGetForegroundProcess(out int fgPid, out string fgName);

        lock (_lock)
        {
            // Dọn các buffer quá cũ không còn khung hình nào trong 8 giây
            var stalePids = _buffers
                .Where(kv => nowSec - kv.Value.LastSeenWallSec > 8.0)
                .Select(kv => kv.Key)
                .ToList();
            foreach (int sp in stalePids)
                _buffers.Remove(sp);

            // Kiểm tra Lossless Scaling có đang tạo khung hình ở lớp phủ ngoài hay không
            var lsEntry = _buffers.FirstOrDefault(kv =>
                kv.Value.ProcessName.Equals("LosslessScaling", StringComparison.OrdinalIgnoreCase) &&
                nowSec - kv.Value.LastSeenWallSec <= 1.8);

            double? lsDisplayFps = lsEntry.Value != null
                ? ComputeRecentDisplayedFps(lsEntry.Value, nowSec)
                : null;

            int targetPid = -1;
            string targetName = string.Empty;
            ProcessFrameBuffer? targetBuffer = null;

            if (hasFg)
            {
                if (fgName.Equals("LosslessScaling", StringComparison.OrdinalIgnoreCase))
                {
                    // Khi Lossless Scaling phủ trên cùng, chọn game 3D đang dựng hình tích cực nhất ngay bên dưới
                    var bestGame = _buffers
                        .Where(kv => !kv.Value.ProcessName.Equals("LosslessScaling", StringComparison.OrdinalIgnoreCase) &&
                                     !ExcludedProcesses.Contains(kv.Value.ProcessName) &&
                                     nowSec - kv.Value.LastSeenWallSec <= 2.0)
                        .OrderByDescending(kv => CountRecentSamples(kv.Value, nowSec, 1.5))
                        .FirstOrDefault();

                    if (bestGame.Value != null && CountRecentSamples(bestGame.Value, nowSec, 1.5) >= 2)
                    {
                        targetPid = bestGame.Key;
                        targetBuffer = bestGame.Value;
                        targetName = string.IsNullOrWhiteSpace(bestGame.Value.ProcessName)
                            ? $"PID {bestGame.Key}"
                            : bestGame.Value.ProcessName;
                    }
                }
                else if (!ExcludedProcesses.Contains(fgName) &&
                         _buffers.TryGetValue(fgPid, out var fgBuf) &&
                         nowSec - fgBuf.LastSeenWallSec <= 2.2 &&
                         CountRecentSamples(fgBuf, nowSec, 1.5) >= 2)
                {
                    targetPid = fgPid;
                    targetBuffer = fgBuf;
                    targetName = fgName;
                }
            }

            if (targetBuffer == null || targetPid <= 0)
            {
                if (_activeGamePid != -1)
                {
                    _activeGamePid = -1;
                    _activeGameName = string.Empty;
                    _graphHistory.Clear();
                }

                var statusKind = GetStatusKind(false);
                return new FpsSnapshot
                {
                    IsPresentMonAvailable = IsPresentMonAvailable,
                    IsPresentMonRunning = _isRunning,
                    StatusKind = statusKind,
                    StatusErrorDetail = _lastError,
                    StatusText = FormatStatusText(statusKind, _lastError, string.Empty, null),
                    IsGameActive = false,
                    ActiveGameName = string.Empty,
                    GraphHistory = Array.Empty<FpsGraphPoint>()
                };
            }

            // Đổi sang game mới -> tự reset cửa sổ trượt & đồ thị
            if (_activeGamePid != targetPid)
            {
                _activeGamePid = targetPid;
                _activeGameName = targetName;
                _graphHistory.Clear();
            }

            // Prune 60s window
            double cutoff = Math.Max(_manualResetWallSec, nowSec - SlidingWindowSeconds);
            while (targetBuffer.Samples.Count > 0 && targetBuffer.Samples.Peek().WallTimeSec < cutoff)
                targetBuffer.Samples.Dequeue();

            var allWindowSamples = targetBuffer.Samples.ToList();
            if (allWindowSamples.Count == 0)
            {
                var statusKind = GetStatusKind(false);
                return new FpsSnapshot
                {
                    IsPresentMonAvailable = IsPresentMonAvailable,
                    IsPresentMonRunning = _isRunning,
                    StatusKind = statusKind,
                    StatusErrorDetail = _lastError,
                    StatusText = FormatStatusText(statusKind, _lastError, string.Empty, null),
                    IsGameActive = false
                };
            }

            // Tính Base FPS (trong 1 giây gần nhất)
            var recentSamples = allWindowSamples
                .Where(s => s.WallTimeSec >= nowSec - RecentFpsWindowSeconds)
                .ToList();
            if (recentSamples.Count == 0)
                recentSamples = allWindowSamples.TakeLast(Math.Min(10, allWindowSamples.Count)).ToList();

            var appRecentSamples = recentSamples.Where(s => !s.IsGenerated).ToList();
            if (appRecentSamples.Count == 0)
                appRecentSamples = recentSamples;

            double avgRecentBaseMs = appRecentSamples.Average(s => s.BaseFrameTimeMs);
            double baseFps = avgRecentBaseMs > 0.05 ? 1000.0 / avgRecentBaseMs : 0;

            // Tính FrameGen FPS (phát hiện cả Lossless Scaling lẫn in-game / driver FrameGen)
            double? frameGenFps = null;
            string? fgSource = null;

            if (lsDisplayFps.HasValue && lsDisplayFps.Value > baseFps * 1.15 && lsDisplayFps.Value >= 20)
            {
                frameGenFps = lsDisplayFps.Value;
                fgSource = "Lossless Scaling";
            }
            else
            {
                bool hasGeneratedFlag = recentSamples.Any(s => s.IsGenerated);
                double? displayedFps = ComputeRecentDisplayedFps(targetBuffer, nowSec);

                if (hasGeneratedFlag && displayedFps.HasValue && displayedFps.Value > baseFps * 1.08)
                {
                    frameGenFps = displayedFps.Value;
                    fgSource = "FrameGen";
                }
                else if (displayedFps.HasValue && displayedFps.Value > baseFps * 1.18 && displayedFps.Value >= 30)
                {
                    frameGenFps = displayedFps.Value;
                    fgSource = "FrameGen";
                }
            }

            // Tính Avg FPS, 1% Low và 0.1% Low trên cửa sổ trượt 60 giây
            var appWindowFrameTimes = allWindowSamples
                .Where(s => !s.IsGenerated)
                .Select(s => s.BaseFrameTimeMs)
                .ToList();
            if (appWindowFrameTimes.Count == 0)
                appWindowFrameTimes = allWindowSamples.Select(s => s.BaseFrameTimeMs).ToList();

            double meanWindowMs = appWindowFrameTimes.Average();
            double avgFps = meanWindowMs > 0.05 ? 1000.0 / meanWindowMs : baseFps;

            double? onePctLow = null;
            double? pointOnePctLow = null;

            if (appWindowFrameTimes.Count >= 5)
            {
                appWindowFrameTimes.Sort((a, b) => b.CompareTo(a)); // Giảm dần: frametime tệ nhất lên đầu
                int n = appWindowFrameTimes.Count;

                int count1 = Math.Max(1, (int)Math.Ceiling(n * 0.01));
                double worst1Ms = appWindowFrameTimes.Take(count1).Average();
                if (worst1Ms > 0.05)
                    onePctLow = Math.Min(avgFps, 1000.0 / worst1Ms);

                int count01 = Math.Max(1, (int)Math.Ceiling(n * 0.001));
                double worst01Ms = appWindowFrameTimes.Take(count01).Average();
                if (worst01Ms > 0.05)
                    pointOnePctLow = Math.Min(onePctLow ?? avgFps, 1000.0 / worst01Ms);
            }

            // Lưu điểm vào lịch sử Đồ thị FPS
            _graphHistory.Enqueue(new FpsGraphPoint(baseFps, frameGenFps));
            while (_graphHistory.Count > MaxGraphPoints)
                _graphHistory.Dequeue();

            var activeKind = GetStatusKind(true);
            return new FpsSnapshot
            {
                IsPresentMonAvailable = IsPresentMonAvailable,
                IsPresentMonRunning = _isRunning,
                StatusKind = activeKind,
                StatusErrorDetail = _lastError,
                StatusText = FormatStatusText(activeKind, _lastError, targetName, fgSource),
                IsGameActive = true,
                ActiveGameName = targetName,
                BaseFps = baseFps,
                FrameGenFps = frameGenFps,
                FrameGenSource = fgSource,
                AvgFps = avgFps,
                OnePercentLowFps = onePctLow,
                PointOnePercentLowFps = pointOnePctLow,
                GraphHistory = _graphHistory.ToArray()
            };
        }
    }

    private static int CountRecentSamples(ProcessFrameBuffer buf, double nowSec, double windowSec)
    {
        int count = 0;
        foreach (var s in buf.Samples)
        {
            if (s.WallTimeSec >= nowSec - windowSec)
                count++;
        }
        return count;
    }

    private static double? ComputeRecentDisplayedFps(ProcessFrameBuffer buf, double nowSec)
    {
        var intervals = new List<double>();
        int displayedCount = 0;

        foreach (var s in buf.Samples)
        {
            if (s.WallTimeSec < nowSec - RecentFpsWindowSeconds)
                continue;
            if (!s.IsDisplayed)
                continue;

            displayedCount++;
            if (s.DisplayIntervalMs.HasValue && s.DisplayIntervalMs.Value > 0.1 && s.DisplayIntervalMs.Value < 1000)
                intervals.Add(s.DisplayIntervalMs.Value);
        }

        if (intervals.Count >= 2)
        {
            double avgDispMs = intervals.Average();
            if (avgDispMs > 0.1)
                return 1000.0 / avgDispMs;
        }

        return displayedCount >= 5 ? displayedCount / RecentFpsWindowSeconds : null;
    }

    private PresentMonStatusKind GetStatusKind(bool isGameActive)
    {
        if (!IsPresentMonAvailable)
            return PresentMonStatusKind.Missing;
        if (!_isRunning)
            return string.IsNullOrWhiteSpace(_lastError) ? PresentMonStatusKind.Stopped : PresentMonStatusKind.Error;
        return isGameActive ? PresentMonStatusKind.Tracking : PresentMonStatusKind.Waiting;
    }

    public static string FormatStatusText(PresentMonStatusKind kind, string errorDetail, string gameName, string? fgSource)
    {
        var loc = LocalizationService.Instance;
        return kind switch
        {
            PresentMonStatusKind.Missing => loc["PmStatusMissing"],
            PresentMonStatusKind.Stopped => loc["PmStatusStopped"],
            PresentMonStatusKind.Error => loc.Format("PmStatusError", errorDetail),
            PresentMonStatusKind.Tracking => string.IsNullOrWhiteSpace(fgSource)
                ? loc.Format("PmStatusTracking", gameName)
                : loc.Format("PmStatusTrackingFg", gameName, fgSource),
            _ => loc["PmStatusWaiting"]
        };
    }

    private void StopPresentMonProcess()
    {
        _isRunning = false;
        if (_presentMonProcess != null)
        {
            try
            {
                if (!_presentMonProcess.HasExited)
                    _presentMonProcess.Kill(entireProcessTree: true);
            }
            catch { }
            try { _presentMonProcess.Dispose(); } catch { }
            _presentMonProcess = null;
        }
    }

    public void Dispose()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            try { _publishTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }
            _cts.Dispose();
            _cts = null;
        }

        StopPresentMonProcess();

        // Đóng sạch ETW session nếu còn tồn tại
        if (IsPresentMonAvailable)
        {
            try
            {
                var psi = new ProcessStartInfo(_presentMonPath, "--session_name WinHardwareMonitor_PM --terminate_existing_session")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var cleanup = Process.Start(psi);
                cleanup?.WaitForExit(1000);
            }
            catch { }
        }
    }
}
