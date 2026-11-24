using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;
using WinHardwareMonitor.Models;
using Wpf.Ui.Controls;

namespace WinHardwareMonitor.Services;

public sealed class HardwareMonitorService : IDisposable
{
    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (var sub in hardware.SubHardware)
                sub.Accept(this);
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

    private sealed class GpuDescriptor
    {
        public int Index { get; init; }
        public IHardware Hardware { get; init; } = null!;
        public GpuVendor Vendor { get; init; }
        public bool IsDiscrete { get; init; }
        public string ShortName { get; init; } = string.Empty;
        public string ShortPrefix { get; init; } = string.Empty;
        public string GroupTitle { get; init; } = string.Empty;
    }

    private sealed class PhysicalDiskDescriptor : IDisposable
    {
        public int DiskIndex { get; init; }
        public string ModelName { get; init; } = string.Empty;
        public string DriveLetters { get; init; } = string.Empty;
        public string ShortTag { get; init; } = string.Empty;
        public IHardware? LhmHardware { get; set; }
        public PerformanceCounter? ReadCounter { get; set; }
        public PerformanceCounter? WriteCounter { get; set; }

        public void Dispose()
        {
            try { ReadCounter?.Dispose(); } catch { }
            try { WriteCounter?.Dispose(); } catch { }
            ReadCounter = null;
            WriteCounter = null;
        }
    }

    private readonly UpdateVisitor _visitor = new();
    private Computer? _computer;
    private readonly List<GpuDescriptor> _gpus = new();
    private readonly List<DetectedGpuInfo> _detectedGpus = new();
    private readonly List<PhysicalDiskDescriptor> _disks = new();
    private readonly List<MetricItem> _discoveredMetrics = new();

    private PerformanceCounter? _totalDiskReadCounter;
    private PerformanceCounter? _totalDiskWriteCounter;

    private CancellationTokenSource? _cts;
    private Task? _pollingTask;

    // Fallback network speed measurement via .NET NetworkInterface
    private long _lastNetRxBytes;
    private long _lastNetTxBytes;
    private long _lastNetTimestamp;

    public IReadOnlyList<MetricItem> DiscoveredMetrics => _discoveredMetrics;
    public IReadOnlyList<DetectedGpuInfo> DetectedGpus => _detectedGpus;
    public IReadOnlyList<string> DefaultMetricIds { get; private set; } = Array.Empty<string>();
    public bool IsInitialized { get; private set; }
    public string? InitError { get; private set; }

    public event Action<IReadOnlyDictionary<string, string>>? SnapshotReady;

    public void Initialize()
    {
        if (IsInitialized)
            return;

        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsNetworkEnabled = true,
                IsStorageEnabled = true,
                IsMotherboardEnabled = false,
                IsControllerEnabled = false,
                IsBatteryEnabled = false,
                IsPsuEnabled = false
            };

            _computer.Open();
            _computer.Accept(_visitor);
        }
        catch (Exception ex)
        {
            InitError = ex.Message;
        }

        SampleFallbackNetworkTotals(out _lastNetRxBytes, out _lastNetTxBytes);
        _lastNetTimestamp = Stopwatch.GetTimestamp();

        BuildMetricCatalog();
        IsInitialized = true;
    }

    public void Start()
    {
        if (!IsInitialized)
            Initialize();

        if (_pollingTask != null)
            return;

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _pollingTask = Task.Run(async () =>
        {
            PushSnapshotSafe();

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    PushSnapshotSafe();
                }
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
            }
        }, token);
    }

    private void PushSnapshotSafe()
    {
        try
        {
            var snapshot = ReadSnapshot();
            SnapshotReady?.Invoke(snapshot);
        }
        catch
        {
            // Avoid crashing background thread on transient sensor errors
        }
    }

    private void BuildMetricCatalog()
    {
        _discoveredMetrics.Clear();
        _gpus.Clear();
        _detectedGpus.Clear();
        DisposeDiskCounters();
        _disks.Clear();

        var hardwareList = _computer?.Hardware ?? new List<IHardware>();

        // 1. CPU
        var cpuHw = hardwareList.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        string cpuName = CleanHardwareName(cpuHw?.Name, "Bộ xử lý trung tâm");

        _discoveredMetrics.Add(new MetricItem
        {
            Id = "cpu_load",
            Category = MetricCategory.Cpu,
            GroupKey = "cpu",
            GroupTitle = "CPU",
            GroupSubtitle = cpuName,
            Title = "Tải CPU",
            ShortLabel = "CPU",
            DefaultSymbol = SymbolRegular.DeveloperBoard24,
            Description = "Phần trăm sử dụng tổng thể"
        });
        _discoveredMetrics.Add(new MetricItem
        {
            Id = "cpu_temp",
            Category = MetricCategory.Cpu,
            GroupKey = "cpu",
            GroupTitle = "CPU",
            GroupSubtitle = cpuName,
            Title = "Nhiệt độ CPU",
            ShortLabel = "CPU °C",
            DefaultSymbol = SymbolRegular.Temperature24,
            Description = "Nhiệt độ gói / nhân (°C)"
        });
        _discoveredMetrics.Add(new MetricItem
        {
            Id = "cpu_clock",
            Category = MetricCategory.Cpu,
            GroupKey = "cpu",
            GroupTitle = "CPU",
            GroupSubtitle = cpuName,
            Title = "Xung nhịp CPU",
            ShortLabel = "CPU GHz",
            DefaultSymbol = SymbolRegular.TopSpeed24,
            Description = "Tần số hoạt động trung bình (GHz)"
        });
        _discoveredMetrics.Add(new MetricItem
        {
            Id = "cpu_power",
            Category = MetricCategory.Cpu,
            GroupKey = "cpu",
            GroupTitle = "CPU",
            GroupSubtitle = cpuName,
            Title = "Công suất CPU",
            ShortLabel = "CPU W",
            DefaultSymbol = SymbolRegular.Flash24,
            Description = "Điện năng tiêu thụ (W)"
        });

        // 2. GPUs (Hỗ trợ tách biệt card tích hợp và card rời)
        var gpuHardwares = hardwareList
            .Where(h => h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
            .ToList();

        var classifiedGpus = gpuHardwares
            .Select(h => (Hardware: h, Vendor: DetectGpuVendor(h.HardwareType, h.Name), IsDiscrete: IsDiscreteGpu(h.HardwareType, h.Name)))
            .OrderByDescending(x => x.IsDiscrete)
            .ToList();

        int discreteCount = classifiedGpus.Count(x => x.IsDiscrete);
        int integratedCount = classifiedGpus.Count(x => !x.IsDiscrete);
        int discreteIdx = 0;
        int integratedIdx = 0;

        for (int i = 0; i < classifiedGpus.Count; i++)
        {
            var (hw, vendor, isDiscrete) = classifiedGpus[i];
            string shortName = CleanHardwareName(hw.Name, $"GPU {i + 1}");

            string groupTitle;
            string shortPrefix;
            string vramShort;

            if (classifiedGpus.Count == 1)
            {
                groupTitle = isDiscrete ? "GPU (Card rời)" : "GPU (Card tích hợp)";
                shortPrefix = "GPU";
                vramShort = "VRAM";
            }
            else if (isDiscrete)
            {
                discreteIdx++;
                string suffix = discreteCount > 1 ? $" {discreteIdx}" : string.Empty;
                groupTitle = $"GPU rời{suffix}";
                shortPrefix = $"GPU rời{suffix}";
                vramShort = $"VRAM rời{suffix}";
            }
            else
            {
                integratedIdx++;
                string suffix = integratedCount > 1 ? $" {integratedIdx}" : string.Empty;
                groupTitle = $"GPU tích hợp{suffix}";
                shortPrefix = $"iGPU{suffix}";
                vramShort = $"VRAM iGPU{suffix}";
            }

            var desc = new GpuDescriptor
            {
                Index = i,
                Hardware = hw,
                Vendor = vendor,
                IsDiscrete = isDiscrete,
                ShortName = shortName,
                ShortPrefix = shortPrefix,
                GroupTitle = groupTitle
            };
            _gpus.Add(desc);

            _detectedGpus.Add(new DetectedGpuInfo
            {
                Id = $"gpu_{i}",
                Name = shortName,
                Vendor = vendor,
                IsDiscrete = isDiscrete
            });

            string groupKey = $"gpu_{i}";
            _discoveredMetrics.Add(new MetricItem
            {
                Id = $"gpu_{i}_load",
                Category = MetricCategory.Gpu,
                GroupKey = groupKey,
                GroupTitle = groupTitle,
                GroupSubtitle = shortName,
                Title = $"Tải {shortPrefix}",
                ShortLabel = shortPrefix,
                DefaultSymbol = SymbolRegular.Games24,
                Description = "Phần trăm tải nhân đồ họa"
            });
            _discoveredMetrics.Add(new MetricItem
            {
                Id = $"gpu_{i}_temp",
                Category = MetricCategory.Gpu,
                GroupKey = groupKey,
                GroupTitle = groupTitle,
                GroupSubtitle = shortName,
                Title = $"Nhiệt độ {shortPrefix}",
                ShortLabel = $"{shortPrefix} °C",
                DefaultSymbol = SymbolRegular.Temperature24,
                Description = "Nhiệt độ nhân đồ họa (°C)"
            });
            _discoveredMetrics.Add(new MetricItem
            {
                Id = $"gpu_{i}_vram",
                Category = MetricCategory.Gpu,
                GroupKey = groupKey,
                GroupTitle = groupTitle,
                GroupSubtitle = shortName,
                Title = $"VRAM {shortPrefix}",
                ShortLabel = vramShort,
                DefaultSymbol = SymbolRegular.DesktopPulse24,
                Description = "Bộ nhớ đồ họa đang dùng"
            });
            _discoveredMetrics.Add(new MetricItem
            {
                Id = $"gpu_{i}_power",
                Category = MetricCategory.Gpu,
                GroupKey = groupKey,
                GroupTitle = groupTitle,
                GroupSubtitle = shortName,
                Title = $"Công suất {shortPrefix}",
                ShortLabel = $"{shortPrefix} W",
                DefaultSymbol = SymbolRegular.Flash24,
                Description = "Điện năng tiêu thụ (W)"
            });
        }

        // Bổ sung quét WMI Win32_VideoController để không bỏ sót card tích hợp/rời khi đang ngủ
        SupplementDetectedGpusFromWmi();

        if (_gpus.Count == 0)
        {
            _discoveredMetrics.Add(new MetricItem
            {
                Id = "gpu_0_load",
                Category = MetricCategory.Gpu,
                GroupKey = "gpu_0",
                GroupTitle = "GPU",
                GroupSubtitle = "Không phát hiện cảm biến GPU",
                Title = "Tải GPU",
                ShortLabel = "GPU",
                DefaultSymbol = SymbolRegular.Games24,
                Description = "Phần trăm tải nhân đồ họa"
            });
            _discoveredMetrics.Add(new MetricItem
            {
                Id = "gpu_0_temp",
                Category = MetricCategory.Gpu,
                GroupKey = "gpu_0",
                GroupTitle = "GPU",
                GroupSubtitle = "Không phát hiện cảm biến GPU",
                Title = "Nhiệt độ GPU",
                ShortLabel = "GPU °C",
                DefaultSymbol = SymbolRegular.Temperature24,
                Description = "Nhiệt độ nhân đồ họa (°C)"
            });
            _discoveredMetrics.Add(new MetricItem
            {
                Id = "gpu_0_vram",
                Category = MetricCategory.Gpu,
                GroupKey = "gpu_0",
                GroupTitle = "GPU",
                GroupSubtitle = "Không phát hiện cảm biến GPU",
                Title = "VRAM GPU",
                ShortLabel = "VRAM",
                DefaultSymbol = SymbolRegular.DesktopPulse24,
                Description = "Bộ nhớ đồ họa đang dùng"
            });
            _discoveredMetrics.Add(new MetricItem
            {
                Id = "gpu_0_power",
                Category = MetricCategory.Gpu,
                GroupKey = "gpu_0",
                GroupTitle = "GPU",
                GroupSubtitle = "Không phát hiện cảm biến GPU",
                Title = "Công suất GPU",
                ShortLabel = "GPU W",
                DefaultSymbol = SymbolRegular.Flash24,
                Description = "Điện năng tiêu thụ (W)"
            });
        }

        // 3. RAM
        string ramSubtitle = Win32Helper.TryGetPhysicalMemory(out _, out double totalGb, out _)
            ? $"Bộ nhớ hệ thống ({totalGb:F0} GB)"
            : "Bộ nhớ hệ thống";

        _discoveredMetrics.Add(new MetricItem
        {
            Id = "ram_usage",
            Category = MetricCategory.Ram,
            GroupKey = "ram",
            GroupTitle = "RAM",
            GroupSubtitle = ramSubtitle,
            Title = "RAM đang dùng (GB & %)",
            ShortLabel = "RAM",
            DefaultSymbol = SymbolRegular.Ram20,
            Description = "Dung lượng đã dùng / tổng và phần trăm"
        });
        _discoveredMetrics.Add(new MetricItem
        {
            Id = "ram_percent",
            Category = MetricCategory.Ram,
            GroupKey = "ram",
            GroupTitle = "RAM",
            GroupSubtitle = ramSubtitle,
            Title = "Phần trăm RAM",
            ShortLabel = "RAM %",
            DefaultSymbol = SymbolRegular.Memory16,
            Description = "Chỉ hiển thị tỷ lệ phần trăm gọn"
        });

        // 4. Mạng
        _discoveredMetrics.Add(new MetricItem
        {
            Id = "net_down",
            Category = MetricCategory.Network,
            GroupKey = "network",
            GroupTitle = "Mạng",
            GroupSubtitle = "Lưu lượng thời gian thực",
            Title = "Tốc độ tải xuống",
            ShortLabel = "Tải xuống",
            DefaultSymbol = SymbolRegular.ArrowDownload28,
            Description = "Tốc độ nhận dữ liệu (Download)"
        });
        _discoveredMetrics.Add(new MetricItem
        {
            Id = "net_up",
            Category = MetricCategory.Network,
            GroupKey = "network",
            GroupTitle = "Mạng",
            GroupSubtitle = "Lưu lượng thời gian thực",
            Title = "Tốc độ tải lên",
            ShortLabel = "Tải lên",
            DefaultSymbol = SymbolRegular.ArrowUpload24,
            Description = "Tốc độ gửi dữ liệu (Upload)"
        });

        // 5. Ổ cứng (Tổng tất cả ổ + từng ổ vật lý)
        DiscoverStorageMetrics(hardwareList);

        // 6. FPS & Hiệu năng game (Intel PresentMon 2.x ETW — không chèn DLL, an toàn Anti-Cheat)
        _discoveredMetrics.Add(new MetricItem
        {
            Id = "fps_base",
            Category = MetricCategory.Fps,
            GroupKey = "fps",
            GroupTitle = "FPS & Hiệu năng game",
            GroupSubtitle = "Intel PresentMon 2.x (ETW an toàn Anti-Cheat, cửa sổ trượt 60s)",
            Title = "FPS gốc",
            ShortLabel = "FPS",
            DefaultSymbol = SymbolRegular.Games24,
            Description = "Khung hình thực do game kết xuất (Base FPS)"
        });
        _discoveredMetrics.Add(new MetricItem
        {
            Id = "fps_framegen",
            Category = MetricCategory.Fps,
            GroupKey = "fps",
            GroupTitle = "FPS & Hiệu năng game",
            GroupSubtitle = "Intel PresentMon 2.x (ETW an toàn Anti-Cheat, cửa sổ trượt 60s)",
            Title = "FPS FrameGen",
            ShortLabel = "FPS FG",
            DefaultSymbol = SymbolRegular.TopSpeed24,
            Description = "Khung hình hiển thị tạo thêm (DLSS/FSR/XeSS FG, Lossless Scaling)"
        });
        _discoveredMetrics.Add(new MetricItem
        {
            Id = "fps_avg",
            Category = MetricCategory.Fps,
            GroupKey = "fps",
            GroupTitle = "FPS & Hiệu năng game",
            GroupSubtitle = "Intel PresentMon 2.x (ETW an toàn Anti-Cheat, cửa sổ trượt 60s)",
            Title = "FPS trung bình",
            ShortLabel = "Avg FPS",
            DefaultSymbol = SymbolRegular.Gauge24,
            Description = "Tốc độ khung hình trung bình (60 giây gần nhất)"
        });
        _discoveredMetrics.Add(new MetricItem
        {
            Id = "fps_1low",
            Category = MetricCategory.Fps,
            GroupKey = "fps",
            GroupTitle = "FPS & Hiệu năng game",
            GroupSubtitle = "Intel PresentMon 2.x (ETW an toàn Anti-Cheat, cửa sổ trượt 60s)",
            Title = "1% Low FPS",
            ShortLabel = "1% Low",
            DefaultSymbol = SymbolRegular.DataUsage24,
            Description = "Mức tụt khung hình 1% thấp nhất (độ mượt)"
        });
        _discoveredMetrics.Add(new MetricItem
        {
            Id = "fps_01low",
            Category = MetricCategory.Fps,
            GroupKey = "fps",
            GroupTitle = "FPS & Hiệu năng game",
            GroupSubtitle = "Intel PresentMon 2.x (ETW an toàn Anti-Cheat, cửa sổ trượt 60s)",
            Title = "0.1% Low FPS",
            ShortLabel = "0.1% Low",
            DefaultSymbol = SymbolRegular.ClockAlarm24,
            Description = "Mức khựng sâu 0.1% thấp nhất (stutter)"
        });
        _discoveredMetrics.Add(new MetricItem
        {
            Id = "fps_graph",
            Category = MetricCategory.Fps,
            GroupKey = "fps",
            GroupTitle = "FPS & Hiệu năng game",
            GroupSubtitle = "Intel PresentMon 2.x (ETW an toàn Anti-Cheat, cửa sổ trượt 60s)",
            Title = "Đồ thị FPS",
            ShortLabel = "Đồ thị FPS",
            DefaultSymbol = SymbolRegular.ChartMultiple24,
            Description = "Biểu đồ đường FPS gốc & FrameGen thời gian thực",
            IsGraphMetric = true
        });
        _discoveredMetrics.Add(new MetricItem
        {
            Id = "gpu_tech_badges",
            Category = MetricCategory.Fps,
            GroupKey = "fps",
            GroupTitle = "FPS & Hiệu năng game",
            GroupSubtitle = "Intel PresentMon 2.x (ETW an toàn Anti-Cheat, cửa sổ trượt 60s)",
            Title = "Huy hiệu Công nghệ GPU",
            ShortLabel = "GPU Tech",
            DefaultSymbol = SymbolRegular.Games24,
            Description = "Hiển thị thẻ DLSS, DLAA, Reflex, FSR, AFMF, XeSS, RT...",
            IsGpuBadgesMetric = true
        });

        var defaults = new List<string> { "cpu_load", "cpu_temp", "cpu_clock" };
        if (_gpus.Count > 0)
        {
            defaults.Add("gpu_0_load");
            defaults.Add("gpu_0_temp");
        }
        defaults.Add("ram_usage");
        defaults.Add("disk_total_read");
        defaults.Add("disk_total_write");
        defaults.Add("net_down");
        defaults.Add("net_up");
        DefaultMetricIds = defaults;
    }

    private void DiscoverStorageMetrics(IList<IHardware> hardwareList)
    {
        var lhmStorages = hardwareList
            .Where(h => h.HardwareType == HardwareType.Storage)
            .ToList();

        var wmiDiskModels = QueryWmiDiskModels();

        // Khởi tạo PerformanceCounter cho tổng _Total và từng ổ vật lý
        string[] perfInstances = Array.Empty<string>();
        try
        {
            if (PerformanceCounterCategory.Exists("PhysicalDisk"))
            {
                var cat = new PerformanceCounterCategory("PhysicalDisk");
                perfInstances = cat.GetInstanceNames();

                if (perfInstances.Any(n => string.Equals(n, "_Total", StringComparison.OrdinalIgnoreCase)))
                {
                    _totalDiskReadCounter = TryCreateCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
                    _totalDiskWriteCounter = TryCreateCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
                }
            }
        }
        catch
        {
            // Bỏ qua nếu hệ thống tắt PerformanceCounter
        }

        // Xây dựng danh sách ổ vật lý từ PerformanceCounter instances ("0 C:", "1 D: E:", ...)
        var usedLhm = new HashSet<IHardware>();
        var physicalInstances = perfInstances
            .Where(name => !string.Equals(name, "_Total", StringComparison.OrdinalIgnoreCase))
            .Select(ParsePhysicalDiskInstance)
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .OrderBy(x => x.DiskIndex)
            .ToList();

        foreach (var inst in physicalInstances)
        {
            string model = wmiDiskModels.TryGetValue(inst.DiskIndex, out var wmiModel)
                ? wmiModel
                : $"Ổ đĩa vật lý {inst.DiskIndex}";

            // Khớp với LibreHardwareMonitor Storage hardware tương ứng nếu có
            var matchedLhm = lhmStorages.FirstOrDefault(h =>
                !usedLhm.Contains(h) &&
                !string.IsNullOrWhiteSpace(h.Name) &&
                (model.Contains(h.Name, StringComparison.OrdinalIgnoreCase) ||
                 h.Name.Contains(model, StringComparison.OrdinalIgnoreCase)));

            if (matchedLhm == null && inst.DiskIndex < lhmStorages.Count && !usedLhm.Contains(lhmStorages[inst.DiskIndex]))
                matchedLhm = lhmStorages[inst.DiskIndex];

            if (matchedLhm != null)
            {
                usedLhm.Add(matchedLhm);
                if (model.StartsWith("Ổ đĩa vật lý", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(matchedLhm.Name))
                    model = CleanHardwareName(matchedLhm.Name, model);
            }

            string letters = inst.DriveLetters;
            string shortTag = BuildShortDiskTag(inst.DiskIndex, letters);

            var desc = new PhysicalDiskDescriptor
            {
                DiskIndex = inst.DiskIndex,
                ModelName = CleanHardwareName(model, $"Ổ đĩa {inst.DiskIndex}"),
                DriveLetters = letters,
                ShortTag = shortTag,
                LhmHardware = matchedLhm,
                ReadCounter = TryCreateCounter("PhysicalDisk", "Disk Read Bytes/sec", inst.InstanceName),
                WriteCounter = TryCreateCounter("PhysicalDisk", "Disk Write Bytes/sec", inst.InstanceName)
            };
            _disks.Add(desc);
        }

        // Nếu không lấy được PerformanceCounter instances, dùng trực tiếp danh sách Storage từ LibreHardwareMonitor
        if (_disks.Count == 0)
        {
            for (int i = 0; i < lhmStorages.Count; i++)
            {
                var hw = lhmStorages[i];
                _disks.Add(new PhysicalDiskDescriptor
                {
                    DiskIndex = i,
                    ModelName = CleanHardwareName(hw.Name, $"Ổ đĩa {i}"),
                    DriveLetters = string.Empty,
                    ShortTag = $"Ổ {i}",
                    LhmHardware = hw
                });
            }
        }

        // Nhóm 1: Tổng tất cả ổ cứng
        string totalSubtitle = _disks.Count > 0
            ? $"Tổng hợp tất cả {_disks.Count} ổ vật lý (MB/s)"
            : "Tốc độ đọc / ghi tổng thể (MB/s)";

        _discoveredMetrics.Add(new MetricItem
        {
            Id = "disk_total_read",
            Category = MetricCategory.Storage,
            GroupKey = "storage_total",
            GroupTitle = "Ổ cứng — Tổng tất cả ổ",
            GroupSubtitle = totalSubtitle,
            Title = "Đọc ổ cứng (Tổng)",
            ShortLabel = "Đọc ổ",
            DefaultSymbol = SymbolRegular.HardDrive24,
            Description = "Tổng tốc độ đọc các ổ cứng (MB/s)"
        });
        _discoveredMetrics.Add(new MetricItem
        {
            Id = "disk_total_write",
            Category = MetricCategory.Storage,
            GroupKey = "storage_total",
            GroupTitle = "Ổ cứng — Tổng tất cả ổ",
            GroupSubtitle = totalSubtitle,
            Title = "Ghi ổ cứng (Tổng)",
            ShortLabel = "Ghi ổ",
            DefaultSymbol = SymbolRegular.Storage24,
            Description = "Tổng tốc độ ghi các ổ cứng (MB/s)"
        });

        // Nhóm 2+: Riêng từng ổ vật lý trên máy
        foreach (var disk in _disks)
        {
            string letterSuffix = string.IsNullOrWhiteSpace(disk.DriveLetters) ? string.Empty : $" ({disk.DriveLetters})";
            string groupKey = $"storage_{disk.DiskIndex}";
            string groupTitle = $"Ổ cứng {disk.DiskIndex}{letterSuffix}";

            _discoveredMetrics.Add(new MetricItem
            {
                Id = $"disk_{disk.DiskIndex}_read",
                Category = MetricCategory.Storage,
                GroupKey = groupKey,
                GroupTitle = groupTitle,
                GroupSubtitle = disk.ModelName,
                Title = $"Đọc {disk.ShortTag}",
                ShortLabel = $"Đọc {disk.ShortTag}",
                DefaultSymbol = SymbolRegular.HardDrive24,
                Description = $"Tốc độ đọc {disk.ModelName} (MB/s)"
            });
            _discoveredMetrics.Add(new MetricItem
            {
                Id = $"disk_{disk.DiskIndex}_write",
                Category = MetricCategory.Storage,
                GroupKey = groupKey,
                GroupTitle = groupTitle,
                GroupSubtitle = disk.ModelName,
                Title = $"Ghi {disk.ShortTag}",
                ShortLabel = $"Ghi {disk.ShortTag}",
                DefaultSymbol = SymbolRegular.Storage24,
                Description = $"Tốc độ ghi {disk.ModelName} (MB/s)"
            });
        }
    }

    private void SupplementDetectedGpusFromWmi()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterCompatibility FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                string rawName = obj["Name"]?.ToString() ?? string.Empty;
                string compat = obj["AdapterCompatibility"]?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(rawName))
                    continue;

                if (rawName.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase) ||
                    rawName.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                    rawName.Contains("Parsec", StringComparison.OrdinalIgnoreCase) ||
                    rawName.Contains("Indirect", StringComparison.OrdinalIgnoreCase) ||
                    rawName.Contains("Remote", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string cleanName = CleanHardwareName(rawName, rawName);
                bool alreadyPresent = _detectedGpus.Any(g =>
                    g.Name.Equals(cleanName, StringComparison.OrdinalIgnoreCase) ||
                    g.Name.Contains(cleanName, StringComparison.OrdinalIgnoreCase) ||
                    cleanName.Contains(g.Name, StringComparison.OrdinalIgnoreCase));

                if (alreadyPresent)
                    continue;

                var vendor = DetectGpuVendorFromStrings(cleanName, compat);
                if (vendor == GpuVendor.Other)
                    continue;

                var hwType = vendor switch
                {
                    GpuVendor.Nvidia => HardwareType.GpuNvidia,
                    GpuVendor.Amd => HardwareType.GpuAmd,
                    GpuVendor.Intel => HardwareType.GpuIntel,
                    _ => HardwareType.GpuIntel
                };

                _detectedGpus.Add(new DetectedGpuInfo
                {
                    Id = $"wmi_gpu_{_detectedGpus.Count}",
                    Name = cleanName,
                    Vendor = vendor,
                    IsDiscrete = IsDiscreteGpu(hwType, cleanName)
                });
            }
        }
        catch
        {
            // Ignore WMI query errors
        }
    }

    private Dictionary<string, string> ReadSnapshot()
    {
        var result = new Dictionary<string, string>();

        if (_computer != null)
        {
            _computer.Accept(_visitor);
        }

        var hardwareList = _computer?.Hardware ?? new List<IHardware>();

        // --- CPU ---
        var cpuHw = hardwareList.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        if (cpuHw != null)
        {
            var sensors = GetAllSensors(cpuHw);

            var cpuLoad = FindSensor(sensors, SensorType.Load, "CPU Total")
                          ?? AverageSensors(sensors, SensorType.Load, s => s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase));
            result["cpu_load"] = cpuLoad.HasValue ? $"{cpuLoad.Value:F0}%" : "--";

            var cpuTemp = FindSensor(sensors, SensorType.Temperature,
                              "CPU Package", "Core (Tctl/Tdie)", "Tctl/Tdie", "Core Average", "Core Max", "Package")
                          ?? MaxPositiveSensor(sensors, SensorType.Temperature);
            result["cpu_temp"] = cpuTemp.HasValue ? $"{cpuTemp.Value:F0}°C" : "--";

            var coreClocks = sensors
                .Where(s => s.SensorType == SensorType.Clock
                            && s.Value.HasValue
                            && s.Value.Value > 100
                            && !s.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase)
                            && !s.Name.Contains("Effective", StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Value!.Value)
                .ToList();

            if (coreClocks.Count == 0)
            {
                coreClocks = sensors
                    .Where(s => s.SensorType == SensorType.Clock
                                && s.Value.HasValue
                                && s.Value.Value > 100
                                && !s.Name.Contains("Bus", StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Value!.Value)
                    .ToList();
            }

            result["cpu_clock"] = coreClocks.Count > 0
                ? $"{coreClocks.Average() / 1000.0:F2} GHz"
                : "--";

            var cpuPower = FindSensor(sensors, SensorType.Power, "CPU Package", "Package", "CPU Cores", "Core")
                           ?? FirstValidSensor(sensors, SensorType.Power);
            result["cpu_power"] = cpuPower.HasValue ? $"{cpuPower.Value:F1} W" : "--";
        }
        else
        {
            result["cpu_load"] = "--";
            result["cpu_temp"] = "--";
            result["cpu_clock"] = "--";
            result["cpu_power"] = "--";
        }

        // --- GPUs ---
        foreach (var gpu in _gpus)
        {
            var sensors = GetAllSensors(gpu.Hardware);

            var gpuLoad = FindSensor(sensors, SensorType.Load, "GPU Core", "D3D 3D")
                          ?? FirstValidSensor(sensors, SensorType.Load, s =>
                              s.Name.Contains("Core", StringComparison.OrdinalIgnoreCase) ||
                              s.Name.Contains("3D", StringComparison.OrdinalIgnoreCase));
            result[$"gpu_{gpu.Index}_load"] = gpuLoad.HasValue ? $"{gpuLoad.Value:F0}%" : "--";

            var gpuTemp = FindSensor(sensors, SensorType.Temperature, "GPU Core", "GPU Hot Spot", "GPU VR SoC")
                          ?? MaxPositiveSensor(sensors, SensorType.Temperature);
            result[$"gpu_{gpu.Index}_temp"] = gpuTemp.HasValue ? $"{gpuTemp.Value:F0}°C" : "--";

            result[$"gpu_{gpu.Index}_vram"] = FormatGpuVram(sensors);

            var gpuPower = FindSensor(sensors, SensorType.Power, "GPU Package", "GPU Power", "GPU Core", "GPU Total")
                           ?? FirstValidSensor(sensors, SensorType.Power);
            result[$"gpu_{gpu.Index}_power"] = gpuPower.HasValue ? $"{gpuPower.Value:F1} W" : "--";
        }

        if (_gpus.Count == 0)
        {
            result["gpu_0_load"] = "--";
            result["gpu_0_temp"] = "--";
            result["gpu_0_vram"] = "--";
            result["gpu_0_power"] = "--";
        }

        // --- RAM ---
        var ramHw = hardwareList.FirstOrDefault(h =>
                        h.HardwareType == HardwareType.Memory &&
                        !h.Name.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
                    ?? hardwareList.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);

        double? ramUsed = null;
        double? ramTotal = null;
        double? ramPercent = null;

        if (ramHw != null)
        {
            var sensors = GetAllSensors(ramHw);
            var usedSensor = FindSensor(sensors, SensorType.Data, "Memory Used");
            var availSensor = FindSensor(sensors, SensorType.Data, "Memory Available");
            var loadSensor = FindSensor(sensors, SensorType.Load, "Memory");

            if (usedSensor.HasValue && availSensor.HasValue && (usedSensor.Value + availSensor.Value) > 0.1)
            {
                ramUsed = usedSensor.Value;
                ramTotal = usedSensor.Value + availSensor.Value;
                ramPercent = loadSensor ?? (ramUsed.Value / ramTotal.Value * 100.0);
            }
        }

        if ((!ramUsed.HasValue || !ramTotal.HasValue) &&
            Win32Helper.TryGetPhysicalMemory(out double winUsed, out double winTotal, out double winPct))
        {
            ramUsed = winUsed;
            ramTotal = winTotal;
            ramPercent = winPct;
        }

        if (ramUsed.HasValue && ramTotal.HasValue && ramPercent.HasValue)
        {
            result["ram_usage"] = $"{ramUsed.Value:F1}/{ramTotal.Value:F0} GB ({ramPercent.Value:F0}%)";
            result["ram_percent"] = $"{ramPercent.Value:F0}%";
        }
        else
        {
            result["ram_usage"] = "--";
            result["ram_percent"] = "--";
        }

        // --- Network ---
        double downBps = 0;
        double upBps = 0;
        bool hasLhmNet = false;

        var netHardwares = hardwareList
            .Where(h => h.HardwareType == HardwareType.Network &&
                        !h.Name.Contains("Loopback", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var net in netHardwares)
        {
            var sensors = GetAllSensors(net);
            var down = FindSensor(sensors, SensorType.Throughput, "Download Speed");
            var up = FindSensor(sensors, SensorType.Throughput, "Upload Speed");

            if (down.HasValue)
            {
                hasLhmNet = true;
                downBps += Math.Max(0, down.Value);
            }
            if (up.HasValue)
            {
                hasLhmNet = true;
                upBps += Math.Max(0, up.Value);
            }
        }

        if (!hasLhmNet)
        {
            ReadFallbackNetworkSpeed(out downBps, out upBps);
        }
        else
        {
            SampleFallbackNetworkTotals(out _lastNetRxBytes, out _lastNetTxBytes);
            _lastNetTimestamp = Stopwatch.GetTimestamp();
        }

        result["net_down"] = $"↓ {FormatSpeed(downBps)}";
        result["net_up"] = $"↑ {FormatSpeed(upBps)}";

        // --- Storage Read / Write (MB/s) ---
        double sumLhmReadBps = 0;
        double sumLhmWriteBps = 0;
        int lhmDiskCountWithSensors = 0;
        double sumAnyReadBps = 0;
        double sumAnyWriteBps = 0;

        foreach (var disk in _disks)
        {
            double? readBps = null;
            double? writeBps = null;

            if (disk.LhmHardware != null)
            {
                var sensors = GetAllSensors(disk.LhmHardware);
                var lhmRead = FindSensor(sensors, SensorType.Throughput, "Read Rate");
                var lhmWrite = FindSensor(sensors, SensorType.Throughput, "Write Rate");

                if (lhmRead.HasValue)
                    readBps = Math.Max(0, lhmRead.Value);
                if (lhmWrite.HasValue)
                    writeBps = Math.Max(0, lhmWrite.Value);

                if (readBps.HasValue && writeBps.HasValue)
                {
                    lhmDiskCountWithSensors++;
                    sumLhmReadBps += readBps.Value;
                    sumLhmWriteBps += writeBps.Value;
                }
            }

            // Fallback sang Windows PerformanceCounter nếu cảm biến LibreHardwareMonitor chưa có
            if (!readBps.HasValue)
                readBps = TryReadCounter(disk.ReadCounter);
            if (!writeBps.HasValue)
                writeBps = TryReadCounter(disk.WriteCounter);

            if (readBps.HasValue) sumAnyReadBps += readBps.Value;
            if (writeBps.HasValue) sumAnyWriteBps += writeBps.Value;

            result[$"disk_{disk.DiskIndex}_read"] = readBps.HasValue ? FormatMegabytesPerSec(readBps.Value) : "--";
            result[$"disk_{disk.DiskIndex}_write"] = writeBps.HasValue ? FormatMegabytesPerSec(writeBps.Value) : "--";
        }

        double? totalReadBps = null;
        double? totalWriteBps = null;

        if (_disks.Count > 0 && lhmDiskCountWithSensors == _disks.Count)
        {
            totalReadBps = sumLhmReadBps;
            totalWriteBps = sumLhmWriteBps;
        }
        else
        {
            totalReadBps = TryReadCounter(_totalDiskReadCounter) ?? (_disks.Count > 0 ? sumAnyReadBps : null);
            totalWriteBps = TryReadCounter(_totalDiskWriteCounter) ?? (_disks.Count > 0 ? sumAnyWriteBps : null);
        }

        result["disk_total_read"] = totalReadBps.HasValue ? FormatMegabytesPerSec(totalReadBps.Value) : "--";
        result["disk_total_write"] = totalWriteBps.HasValue ? FormatMegabytesPerSec(totalWriteBps.Value) : "--";

        return result;
    }

    private static string FormatMegabytesPerSec(double bytesPerSec)
    {
        double mbps = Math.Max(0, bytesPerSec) / (1024.0 * 1024.0);
        return mbps >= 100 ? $"{mbps:F0} MB/s" : $"{mbps:F1} MB/s";
    }

    private static PerformanceCounter? TryCreateCounter(string category, string counterName, string instanceName)
    {
        try
        {
            var pc = new PerformanceCounter(category, counterName, instanceName, readOnly: true);
            _ = pc.NextValue(); // Prime first reading
            return pc;
        }
        catch
        {
            return null;
        }
    }

    private static double? TryReadCounter(PerformanceCounter? counter)
    {
        if (counter == null)
            return null;
        try
        {
            return Math.Max(0, counter.NextValue());
        }
        catch
        {
            return null;
        }
    }

    private static (int DiskIndex, string DriveLetters, string InstanceName)? ParsePhysicalDiskInstance(string instanceName)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
            return null;

        var parts = instanceName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], out int idx))
            return null;

        string letters = parts.Length > 1 ? parts[1].Trim() : string.Empty;
        return (idx, letters, instanceName);
    }

    private static string BuildShortDiskTag(int diskIndex, string driveLetters)
    {
        if (!string.IsNullOrWhiteSpace(driveLetters))
        {
            var tokens = driveLetters.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 1)
                return tokens[0]; // e.g., "C:"
            if (tokens.Length > 1)
                return $"Ổ {diskIndex} ({tokens[0]})";
        }
        return $"Ổ {diskIndex}";
    }

    private static Dictionary<int, string> QueryWmiDiskModels()
    {
        var map = new Dictionary<int, string>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Index, Model FROM Win32_DiskDrive");
            foreach (ManagementObject obj in searcher.Get())
            {
                if (obj["Index"] != null && int.TryParse(obj["Index"].ToString(), out int idx))
                {
                    string model = obj["Model"]?.ToString()?.Trim() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(model))
                        map[idx] = model;
                }
            }
        }
        catch
        {
            // Ignore WMI query errors
        }
        return map;
    }

    private static string FormatGpuVram(List<ISensor> sensors)
    {
        double? usedMb = FindSensor(sensors, SensorType.SmallData,
            "GPU Memory Used", "D3D Dedicated Memory Used", "D3D Shared Memory Used");
        double? totalMb = FindSensor(sensors, SensorType.SmallData, "GPU Memory Total");

        if (usedMb.HasValue)
        {
            if (totalMb.HasValue && totalMb.Value > 0)
                return $"{usedMb.Value / 1024.0:F1}/{totalMb.Value / 1024.0:F0} GB";

            return usedMb.Value >= 1024
                ? $"{usedMb.Value / 1024.0:F2} GB"
                : $"{usedMb.Value:F0} MB";
        }

        double? usedGb = FindSensor(sensors, SensorType.Data,
            "GPU Memory Used", "D3D Dedicated Memory Used", "D3D Shared Memory Used");
        double? totalGb = FindSensor(sensors, SensorType.Data, "GPU Memory Total");

        if (usedGb.HasValue)
        {
            if (totalGb.HasValue && totalGb.Value > 0)
                return $"{usedGb.Value:F1}/{totalGb.Value:F0} GB";
            return $"{usedGb.Value:F2} GB";
        }

        return "--";
    }

    private static string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec < 1024)
            return $"{bytesPerSec:F0} B/s";
        double kb = bytesPerSec / 1024.0;
        if (kb < 1024)
            return kb >= 100 ? $"{kb:F0} KB/s" : $"{kb:F1} KB/s";
        double mb = kb / 1024.0;
        if (mb < 1024)
            return mb >= 100 ? $"{mb:F0} MB/s" : $"{mb:F1} MB/s";
        double gb = mb / 1024.0;
        return $"{gb:F2} GB/s";
    }

    private void ReadFallbackNetworkSpeed(out double downBps, out double upBps)
    {
        long now = Stopwatch.GetTimestamp();
        SampleFallbackNetworkTotals(out long rx, out long tx);

        double elapsedSec = (now - _lastNetTimestamp) / (double)Stopwatch.Frequency;
        if (elapsedSec > 0.1 && _lastNetTimestamp > 0)
        {
            downBps = Math.Max(0, (rx - _lastNetRxBytes) / elapsedSec);
            upBps = Math.Max(0, (tx - _lastNetTxBytes) / elapsedSec);
        }
        else
        {
            downBps = 0;
            upBps = 0;
        }

        _lastNetRxBytes = rx;
        _lastNetTxBytes = tx;
        _lastNetTimestamp = now;
    }

    private static void SampleFallbackNetworkTotals(out long totalRx, out long totalTx)
    {
        totalRx = 0;
        totalTx = 0;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                var stats = nic.GetIPStatistics();
                totalRx += stats.BytesReceived;
                totalTx += stats.BytesSent;
            }
        }
        catch { }
    }

    private static GpuVendor DetectGpuVendor(HardwareType hardwareType, string? name) => hardwareType switch
    {
        HardwareType.GpuNvidia => GpuVendor.Nvidia,
        HardwareType.GpuAmd => GpuVendor.Amd,
        HardwareType.GpuIntel => GpuVendor.Intel,
        _ => DetectGpuVendorFromStrings(name ?? string.Empty, string.Empty)
    };

    private static GpuVendor DetectGpuVendorFromStrings(string name, string compat)
    {
        string combined = $"{name} {compat}";
        if (combined.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Quadro", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("RTX", StringComparison.OrdinalIgnoreCase))
            return GpuVendor.Nvidia;

        if (combined.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("ATI", StringComparison.OrdinalIgnoreCase))
            return GpuVendor.Amd;

        if (combined.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Arc", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Iris", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("UHD", StringComparison.OrdinalIgnoreCase))
            return GpuVendor.Intel;

        return GpuVendor.Other;
    }

    private static bool IsDiscreteGpu(HardwareType hardwareType, string? rawName)
    {
        string name = rawName ?? string.Empty;

        if (hardwareType == HardwareType.GpuNvidia)
            return true;

        if (hardwareType == HardwareType.GpuIntel)
        {
            if (name.Contains("Arc", StringComparison.OrdinalIgnoreCase) &&
                (name.Contains("A3", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("A5", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("A7", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("B5", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Pro ", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
            return false;
        }

        if (hardwareType == HardwareType.GpuAmd)
        {
            if (name.Contains("RX ", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Radeon PRO", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("FirePro", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Radeon VII", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("R9 ", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("R7 ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        return false;
    }

    private static string CleanHardwareName(string? rawName, string fallback)
    {
        if (string.IsNullOrWhiteSpace(rawName))
            return fallback;

        return rawName
            .Replace("(R)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("(TM)", "", StringComparison.OrdinalIgnoreCase)
            .Replace("  ", " ")
            .Trim();
    }

    private static List<ISensor> GetAllSensors(IHardware hardware)
    {
        var list = new List<ISensor>(hardware.Sensors);
        foreach (var sub in hardware.SubHardware)
            list.AddRange(sub.Sensors);
        return list;
    }

    private static float? FindSensor(List<ISensor> sensors, SensorType type, params string[] preferredNames)
    {
        foreach (var name in preferredNames)
        {
            var exact = sensors.FirstOrDefault(s =>
                s.SensorType == type &&
                s.Value.HasValue &&
                s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (exact?.Value != null)
                return exact.Value;
        }

        foreach (var name in preferredNames)
        {
            var partial = sensors.FirstOrDefault(s =>
                s.SensorType == type &&
                s.Value.HasValue &&
                s.Name.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (partial?.Value != null)
                return partial.Value;
        }

        return null;
    }

    private static float? FirstValidSensor(List<ISensor> sensors, SensorType type, Func<ISensor, bool>? predicate = null)
    {
        var match = sensors.FirstOrDefault(s =>
            s.SensorType == type &&
            s.Value.HasValue &&
            (predicate == null || predicate(s)));
        return match?.Value;
    }

    private static float? AverageSensors(List<ISensor> sensors, SensorType type, Func<ISensor, bool> predicate)
    {
        var values = sensors
            .Where(s => s.SensorType == type && s.Value.HasValue && predicate(s))
            .Select(s => s.Value!.Value)
            .ToList();
        return values.Count > 0 ? values.Average() : null;
    }

    private static float? MaxPositiveSensor(List<ISensor> sensors, SensorType type)
    {
        var values = sensors
            .Where(s => s.SensorType == type && s.Value.HasValue && s.Value.Value > 0)
            .Select(s => s.Value!.Value)
            .ToList();
        return values.Count > 0 ? values.Max() : null;
    }

    private void DisposeDiskCounters()
    {
        try { _totalDiskReadCounter?.Dispose(); } catch { }
        try { _totalDiskWriteCounter?.Dispose(); } catch { }
        _totalDiskReadCounter = null;
        _totalDiskWriteCounter = null;

        foreach (var d in _disks)
            d.Dispose();
    }

    public void Dispose()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            try
            {
                _pollingTask?.Wait(TimeSpan.FromSeconds(1.5));
            }
            catch { }
            _cts.Dispose();
            _cts = null;
            _pollingTask = null;
        }

        DisposeDiskCounters();

        try
        {
            _computer?.Close();
        }
        catch { }
        _computer = null;
    }
}
