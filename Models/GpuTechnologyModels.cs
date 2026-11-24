using System;
using System.Collections.ObjectModel;
using System.Windows.Media;
using WinHardwareMonitor.Services;
using WinHardwareMonitor.ViewModels;

namespace WinHardwareMonitor.Models;

public enum GpuVendor
{
    Nvidia,
    Amd,
    Intel,
    Other
}

public sealed class DetectedGpuInfo : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public GpuVendor Vendor { get; init; }
    public bool IsDiscrete { get; init; }

    public string VendorDisplayName => Vendor switch
    {
        GpuVendor.Nvidia => "NVIDIA",
        GpuVendor.Amd => "AMD",
        GpuVendor.Intel => "Intel",
        _ => LocalizationService.Instance["GpuVendorOther"]
    };

    public string KindBadge => IsDiscrete
        ? LocalizationService.Instance["GpuDiscrete"]
        : LocalizationService.Instance["GpuIntegrated"];

    public string DisplayLabel => $"{VendorDisplayName} — {Name} ({KindBadge})";

    public SolidColorBrush VendorAccentBrush => Vendor switch
    {
        GpuVendor.Nvidia => Freeze("#76B900"),
        GpuVendor.Amd => Freeze("#EF4444"),
        GpuVendor.Intel => Freeze("#38BDF8"),
        _ => Freeze("#94A3B8")
    };

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(VendorDisplayName));
        OnPropertyChanged(nameof(KindBadge));
        OnPropertyChanged(nameof(DisplayLabel));
    }

    private static SolidColorBrush Freeze(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}

public sealed class GpuTechItemViewModel : ObservableObject
{
    private bool _isEnabled;
    private bool _isAutoDetected;
    private GameTechOverrideState _gameOverride = GameTechOverrideState.Auto;

    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string BadgeText { get; init; } = string.Empty;
    public string DescriptionKey { get; init; } = string.Empty;
    public string Description => LocalizationService.Instance[DescriptionKey];
    public GpuVendor Vendor { get; init; }
    public Action? OnToggleChanged { get; set; }
    public Action<GpuTechItemViewModel>? OnOverrideChanged { get; set; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
                OnToggleChanged?.Invoke();
        }
    }

    public bool IsAutoDetected
    {
        get => _isAutoDetected;
        set
        {
            if (SetProperty(ref _isAutoDetected, value))
                OnPropertyChanged(nameof(AutoDetectedLabel));
        }
    }

    public string AutoDetectedLabel => IsAutoDetected
        ? LocalizationService.Instance["TechDetected"]
        : LocalizationService.Instance["TechNotDetected"];

    public GameTechOverrideState GameOverride
    {
        get => _gameOverride;
        set
        {
            if (SetProperty(ref _gameOverride, value))
            {
                OnPropertyChanged(nameof(OverrideLabel));
                OnPropertyChanged(nameof(IsOverrideAuto));
                OnPropertyChanged(nameof(IsOverrideForceOn));
                OnPropertyChanged(nameof(IsOverrideForceOff));
                OnOverrideChanged?.Invoke(this);
            }
        }
    }

    public bool IsOverrideAuto => _gameOverride == GameTechOverrideState.Auto;
    public bool IsOverrideForceOn => _gameOverride == GameTechOverrideState.ForceOn;
    public bool IsOverrideForceOff => _gameOverride == GameTechOverrideState.ForceOff;

    public string OverrideLabel => _gameOverride switch
    {
        GameTechOverrideState.ForceOn => LocalizationService.Instance["OverrideForceOn"],
        GameTechOverrideState.ForceOff => LocalizationService.Instance["OverrideForceOff"],
        _ => LocalizationService.Instance["OverrideAuto"]
    };

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(AutoDetectedLabel));
        OnPropertyChanged(nameof(OverrideLabel));
    }

    public void SetEnabledSilent(bool value)
    {
        if (_isEnabled != value)
        {
            _isEnabled = value;
            OnPropertyChanged(nameof(IsEnabled));
        }
    }

    public void SetOverrideSilent(GameTechOverrideState state)
    {
        if (_gameOverride != state)
        {
            _gameOverride = state;
            OnPropertyChanged(nameof(GameOverride));
            OnPropertyChanged(nameof(OverrideLabel));
            OnPropertyChanged(nameof(IsOverrideAuto));
            OnPropertyChanged(nameof(IsOverrideForceOn));
            OnPropertyChanged(nameof(IsOverrideForceOff));
        }
    }
}

public sealed class GpuTechVendorGroupViewModel : ObservableObject
{
    private bool _isSupported;
    private bool _isVisible = true;
    private string _supportBadgeText = string.Empty;

    public GpuVendor Vendor { get; init; }
    public string Title { get; init; } = string.Empty;
    public string SubtitleKey { get; init; } = string.Empty;
    public string Subtitle => LocalizationService.Instance[SubtitleKey];
    public SolidColorBrush AccentBrush { get; init; } = new(Colors.White);
    public ObservableCollection<GpuTechItemViewModel> Items { get; } = new();

    public bool IsSupported
    {
        get => _isSupported;
        set
        {
            if (SetProperty(ref _isSupported, value))
                OnPropertyChanged(nameof(GroupOpacity));
        }
    }

    public double GroupOpacity => IsSupported ? 1.0 : 0.36;

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public string SupportBadgeText
    {
        get => _supportBadgeText;
        set => SetProperty(ref _supportBadgeText, value);
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Subtitle));
        foreach (var item in Items)
            item.RefreshLocalization();
    }
}

/// <summary>
/// Huy hiệu nhỏ gọn hiển thị trực tiếp trên Widget nổi.
/// </summary>
public sealed class ActiveGpuBadge
{
    public string TechId { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public GpuVendor Vendor { get; init; }
    public SolidColorBrush BackgroundBrush { get; init; } = new(Colors.Transparent);
    public SolidColorBrush BorderBrush { get; init; } = new(Colors.White);
    public SolidColorBrush TextBrush { get; init; } = new(Colors.White);
}
