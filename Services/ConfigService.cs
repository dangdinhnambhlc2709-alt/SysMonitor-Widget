using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using WinHardwareMonitor.Models;

namespace WinHardwareMonitor.Services;

public sealed class ConfigService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _configDirectory;
    private readonly string _configPath;
    private readonly string _legacyConfigPath;
    private readonly object _lock = new();
    private Timer? _debounceTimer;
    private Func<AppConfig>? _pendingSnapshot;

    public string ConfigPath => _configPath;

    public ConfigService()
    {
        _configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SysMonitorWidget");
        _configPath = Path.Combine(_configDirectory, "config.json");

        _legacyConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinHardwareMonitor",
            "config.json");
    }

    public AppConfig Load()
    {
        if (TryDeserializeConfig(_configPath, out var loaded))
        {
            MigrateAndNormalize(loaded!);
            return loaded!;
        }

        if (TryDeserializeConfig(_legacyConfigPath, out var legacy))
        {
            legacy!.ConfigVersion = 1;
            MigrateAndNormalize(legacy);
            WriteConfigFile(legacy);
            return legacy;
        }

        var fallback = new AppConfig();
        MigrateAndNormalize(fallback);
        return fallback;
    }

    private static bool TryDeserializeConfig(string path, out AppConfig? config)
    {
        config = null;
        try
        {
            if (!File.Exists(path))
                return false;

            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return false;

            config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
            return config != null;
        }
        catch
        {
            return false;
        }
    }

    public void ScheduleSave(Func<AppConfig> snapshotFactory)
    {
        lock (_lock)
        {
            _pendingSnapshot = snapshotFactory;
            _debounceTimer ??= new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
            _debounceTimer.Change(300, Timeout.Infinite);
        }
    }

    public void SaveImmediate(AppConfig config)
    {
        lock (_lock)
        {
            _pendingSnapshot = null;
            _debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            WriteConfigFile(config);
        }
    }

    public void Flush()
    {
        AppConfig? snapshot = null;
        lock (_lock)
        {
            if (_pendingSnapshot != null)
            {
                snapshot = _pendingSnapshot();
                _pendingSnapshot = null;
            }
            _debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }

        if (snapshot != null)
            WriteConfigFile(snapshot);
    }

    private void WriteConfigFile(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(_configDirectory);
            string tempPath = _configPath + ".tmp";
            string json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _configPath, overwrite: true);
        }
        catch { }
    }

    private static void MigrateAndNormalize(AppConfig cfg)
    {
        cfg.OverlayOffsetX = Math.Clamp(cfg.OverlayOffsetX, 0, 800);
        cfg.OverlayOffsetY = Math.Clamp(cfg.OverlayOffsetY, 0, 800);
        cfg.GridSnapSize = cfg.GridSnapSize is 4 or 8 or 12 or 16 ? cfg.GridSnapSize : 8;
        cfg.CellPaddingH = Math.Clamp(cfg.CellPaddingH, 0, 24);
        cfg.CellPaddingV = Math.Clamp(cfg.CellPaddingV, 0, 20);
        cfg.GlobalLabelValueGap = Math.Clamp(cfg.GlobalLabelValueGap, 0, 60);

        cfg.FontSize = Math.Clamp(cfg.FontSize, 10, 36);
        cfg.DefaultCellBgOpacity = Math.Clamp(cfg.DefaultCellBgOpacity, 0, 100);
        cfg.DefaultCellBorderOpacity = Math.Clamp(cfg.DefaultCellBorderOpacity, 0, 100);
        cfg.DefaultCellBorderThickness = Math.Clamp(cfg.DefaultCellBorderThickness, 0.5, 4.0);
        cfg.DefaultCellCornerRadius = Math.Clamp(cfg.DefaultCellCornerRadius, 0, 20);

        cfg.OuterFrameBgOpacity = Math.Clamp(cfg.OuterFrameBgOpacity, 0, 100);
        cfg.OuterFrameBorderOpacity = Math.Clamp(cfg.OuterFrameBorderOpacity, 0, 100);
        cfg.OuterFrameBorderThickness = Math.Clamp(cfg.OuterFrameBorderThickness, 0.5, 4.0);
        cfg.OuterFrameCornerRadius = Math.Clamp(cfg.OuterFrameCornerRadius, 0, 28);
        cfg.OuterFramePadding = Math.Clamp(cfg.OuterFramePadding, 0, 24);
        cfg.FpsGraphHeight = Math.Clamp(cfg.FpsGraphHeight, 24, 140);

        if (string.IsNullOrWhiteSpace(cfg.LanguageCode)) cfg.LanguageCode = "vi";
        if (string.IsNullOrWhiteSpace(cfg.FontFamilyName)) cfg.FontFamilyName = "Segoe UI";
        if (string.IsNullOrWhiteSpace(cfg.FontWeightName)) cfg.FontWeightName = "SemiBold";
        if (string.IsNullOrWhiteSpace(cfg.TextColorHex)) cfg.TextColorHex = "#FFFFFF";
        if (string.IsNullOrWhiteSpace(cfg.LabelColorHex)) cfg.LabelColorHex = "#60CDFF";
        if (string.IsNullOrWhiteSpace(cfg.DefaultCellBgColorHex)) cfg.DefaultCellBgColorHex = "#0A0C10";
        if (string.IsNullOrWhiteSpace(cfg.DefaultCellBorderColorHex)) cfg.DefaultCellBorderColorHex = "#FFFFFF";
        if (string.IsNullOrWhiteSpace(cfg.OuterFrameBgColorHex)) cfg.OuterFrameBgColorHex = "#080A0E";
        if (string.IsNullOrWhiteSpace(cfg.OuterFrameBorderColorHex)) cfg.OuterFrameBorderColorHex = "#FFFFFF";
        if (string.IsNullOrWhiteSpace(cfg.FpsGraphBaseColorHex)) cfg.FpsGraphBaseColorHex = "#4ADE80";
        if (string.IsNullOrWhiteSpace(cfg.FpsGraphFrameGenColorHex)) cfg.FpsGraphFrameGenColorHex = "#60CDFF";
        if (string.IsNullOrWhiteSpace(cfg.CustomGpuBadgeColorHex)) cfg.CustomGpuBadgeColorHex = "#60CDFF";

        cfg.OverlayLayers ??= new();
        cfg.EnabledGpuTechIds ??= new();
        cfg.GameTechProfiles ??= new();

        // Chuyển đổi tự động từ v2 (GridPlacements) sang v3 (Canvas Layers) nếu chưa có OverlayLayers
        if (cfg.OverlayLayers.Count == 0 && cfg.GridPlacements is { Count: > 0 })
        {
            int z = 10;
            foreach (var p in cfg.GridPlacements)
            {
                if (string.IsNullOrWhiteSpace(p.MetricId)) continue;
                bool isGraph = p.MetricId == "fps_graph";
                cfg.OverlayLayers.Add(new OverlayLayerConfig
                {
                    LayerId = Guid.NewGuid().ToString("N")[..8],
                    Kind = OverlayLayerKind.Metric,
                    MetricId = p.MetricId,
                    X = 16 + p.Column * 120,
                    Y = 16 + p.Row * 32,
                    Width = isGraph ? Math.Max(184, p.ColumnSpan * 120) : 0,
                    Height = isGraph ? Math.Max(56, cfg.FpsGraphHeight + 20) : 0,
                    ZIndex = z++,
                    Style = p.Style
                });
            }
        }
        else if (cfg.OverlayLayers.Count == 0 && cfg.SelectedMetricIds is { Count: > 0 })
        {
            int z = 10;
            int idx = 0;
            foreach (var id in cfg.SelectedMetricIds)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                int col = idx % 3;
                int row = idx / 3;
                cfg.OverlayLayers.Add(new OverlayLayerConfig
                {
                    LayerId = Guid.NewGuid().ToString("N")[..8],
                    Kind = OverlayLayerKind.Metric,
                    MetricId = id,
                    X = 16 + col * 128,
                    Y = 16 + row * 32,
                    Width = id == "fps_graph" ? 200 : 0,
                    Height = id == "fps_graph" ? 64 : 0,
                    ZIndex = z++
                });
                idx++;
            }
        }

        cfg.ConfigVersion = 3;
        cfg.SelectedMetricIds = null;
        cfg.GridPlacements = null;
    }

    public void Dispose()
    {
        Flush();
        lock (_lock)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }
}
