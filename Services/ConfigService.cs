using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using WinHardwareMonitor.Models;

namespace WinHardwareMonitor.Services;

public sealed class ConfigService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _configDirectory;
    private readonly string _configPath;
    private readonly object _lock = new();
    private Timer? _debounceTimer;
    private Func<AppConfig>? _pendingSnapshot;

    public string ConfigPath => _configPath;

    public ConfigService()
    {
        _configDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinHardwareMonitor");
        _configPath = Path.Combine(_configDirectory, "config.json");
    }

    public AppConfig Load()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                string json = File.ReadAllText(_configPath);
                var loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (loaded != null)
                {
                    Normalize(loaded);
                    return loaded;
                }
            }
        }
        catch
        {
            // Fallback to default config if file is corrupted
        }

        var fallback = new AppConfig();
        Normalize(fallback);
        return fallback;
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
        {
            WriteConfigFile(snapshot);
        }
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
        catch
        {
            // Ignore transient IO failures during auto-save
        }
    }

    private static void Normalize(AppConfig cfg)
    {
        cfg.WidgetWidth = Math.Clamp(cfg.WidgetWidth, 150, 2400);
        cfg.WidgetHeight = Math.Clamp(cfg.WidgetHeight, 42, 1600);
        cfg.BackgroundOpacity = Math.Clamp(cfg.BackgroundOpacity, 0, 100);
        cfg.FontSize = Math.Clamp(cfg.FontSize, 10, 36);
        cfg.CornerRadius = Math.Clamp(cfg.CornerRadius, 0, 32);
        cfg.FpsGraphHeight = Math.Clamp(cfg.FpsGraphHeight, 24, 140);
        cfg.BackgroundImageOpacity = Math.Clamp(cfg.BackgroundImageOpacity, 0, 100);
        if (string.IsNullOrWhiteSpace(cfg.LanguageCode))
            cfg.LanguageCode = "vi";
        if (string.IsNullOrWhiteSpace(cfg.FontFamilyName))
            cfg.FontFamilyName = "Segoe UI";
        if (string.IsNullOrWhiteSpace(cfg.FontWeightName))
            cfg.FontWeightName = "SemiBold";
        if (string.IsNullOrWhiteSpace(cfg.BackgroundColorHex))
            cfg.BackgroundColorHex = "#141419";
        if (string.IsNullOrWhiteSpace(cfg.TextColorHex))
            cfg.TextColorHex = "#FFFFFF";
        if (string.IsNullOrWhiteSpace(cfg.FpsGraphBaseColorHex))
            cfg.FpsGraphBaseColorHex = "#4ADE80";
        if (string.IsNullOrWhiteSpace(cfg.FpsGraphFrameGenColorHex))
            cfg.FpsGraphFrameGenColorHex = "#60CDFF";
        if (string.IsNullOrWhiteSpace(cfg.CustomGpuBadgeColorHex))
            cfg.CustomGpuBadgeColorHex = "#60CDFF";
        cfg.SelectedMetricIds ??= new();
        cfg.MetricStyles ??= new();
        cfg.EnabledGpuTechIds ??= new();
        cfg.GameTechProfiles ??= new();
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
