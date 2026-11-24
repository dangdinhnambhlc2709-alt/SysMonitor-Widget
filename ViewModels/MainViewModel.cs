using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WinHardwareMonitor.Models;
using WinHardwareMonitor.Services;
using Wpf.Ui.Controls;

namespace WinHardwareMonitor.ViewModels;

public sealed class MetricGroupViewModel
{
    public string GroupKey { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public ObservableCollection<MetricItem> Items { get; } = new();
}

public sealed class ColorPreset
{
    public string Name { get; init; } = string.Empty;
    public string Hex { get; init; } = "#FFFFFF";
    public SolidColorBrush Brush { get; init; } = new(Colors.White);
}

public sealed class IconChoice
{
    public string Name { get; init; } = string.Empty;
    public string SymbolName { get; init; } = string.Empty;
    public SymbolRegular Symbol { get; init; }
}

public sealed record StretchModeOption(BackgroundImageStretchMode Mode, string DisplayName);

public sealed record FontWeightOption(string Key, string DisplayName, FontWeight Weight, FontWeight LabelWeight);

public sealed class MainViewModel : ObservableObject
{
    private const double ScaleBaselineWidth = 410.0;
    private const double ScaleBaselineHeight = 190.0;

    private readonly ConfigService _configService;
    private readonly HardwareMonitorService _hardwareService;
    private readonly PresentMonService _presentMonService;
    private readonly GpuTechDetectorService _gpuTechDetectorService;
    private readonly UpdateService _updateService;
    private readonly Dictionary<string, MetricItem> _metricsById = new();
    private readonly Dictionary<string, Dictionary<string, GameTechOverrideState>> _gameTechProfiles = new(StringComparer.OrdinalIgnoreCase);

    private double _widgetLeft = -1;
    private double _widgetTop = -1;
    private double _widgetWidth = ScaleBaselineWidth;
    private double _widgetHeight = ScaleBaselineHeight;
    private double _backgroundOpacity = 78;
    private string _backgroundColorHex = "#141419";
    private string _textColorHex = "#FFFFFF";

    // Typography (Font, Size 10-36, Weight 7 levels)
    private FontFamily _selectedFontFamily = new("Segoe UI");
    private double _fontSize = 13;
    private FontWeightOption _selectedFontWeightOption;

    // Auto-scale content & 3 Win11 Blur Modes
    private bool _autoScaleContent = true;
    private double _contentScaleFactor = 1.0;
    private WidgetBlurMode _blurMode = WidgetBlurMode.Off;

    private double _cornerRadius = 14;
    private bool _isClickThrough;
    private bool _isWidgetTopmost = true;
    private bool _isWidgetVisible = true;
    private bool _isRestoring;
    private bool _isHotkeyRegistered;

    // Ngôn ngữ (10 ngôn ngữ)
    private LanguageOption _selectedLanguage;

    // Ảnh nền PNG cho Widget
    private string _backgroundImagePath = string.Empty;
    private double _backgroundImageOpacity = 85;
    private BackgroundImageStretchMode _backgroundImageStretch = BackgroundImageStretchMode.UniformToFill;
    private ImageBrush? _cachedBackgroundImageBrush;

    // Tab navigation & Per-Metric Customization
    private int _selectedTabIndex;
    private MetricItem? _selectedCustomizationMetric;
    private bool _showOnlyActiveInCustomization;

    // GPU Technologies & Badges & Per-Game Quick Override
    private GpuTechMode _gpuTechMode = GpuTechMode.AutoAllGpus;
    private DetectedGpuInfo? _selectedPrimaryGpu;
    private bool _hideUnsupportedGpuTechs;
    private bool _useCustomGpuBadgeColor;
    private string _customGpuBadgeColorHex = "#60CDFF";
    private string _gpuDetectionStatusText = "Chờ game 3D ở cửa sổ trên cùng để tự động nhận diện công nghệ GPU...";
    private bool _isGpuTechGameActive;
    private string _currentOverrideGameExe = string.Empty;
    private string _manualGameExeInput = string.Empty;
    private HashSet<string> _lastAutoDetectedTechIds = new(StringComparer.OrdinalIgnoreCase);

    // FPS & PresentMon
    private bool _autoHideFpsWhenIdle;
    private bool _autoHideFrameGenWhenInactive;
    private double _fpsGraphHeight = 46;
    private string _fpsGraphBaseColorHex = "#4ADE80";
    private string _fpsGraphFrameGenColorHex = "#60CDFF";
    private string _fpsStatusText = "Đang khởi tạo PresentMon 2.x...";
    private bool _isPresentMonAvailable;
    private bool _isPresentMonRunning;
    private bool _isGameActive;
    private bool _isFrameGenActive;
    private bool _isDownloadingPresentMon;
    private string _presentMonDownloadFeedback = string.Empty;
    private IReadOnlyList<FpsGraphPoint> _fpsGraphHistory = Array.Empty<FpsGraphPoint>();

    // System, Update & Window behavior
    private bool _runAtWindowsStartup;
    private bool _minimizeToTrayOnClose = true;
    private bool _autoCheckUpdatesOnStartup = true;
    private bool _isCheckingUpdates;
    private string _updateStatusMessage = string.Empty;
    private string _startupStatusFeedback = string.Empty;

    public ObservableCollection<MetricGroupViewModel> AvailableGroups { get; } = new();
    public ObservableCollection<MetricItem> AllMetrics { get; } = new();
    public ObservableCollection<MetricItem> ActiveMetrics { get; } = new();
    public ObservableCollection<MetricItem> FilteredCustomizationMetrics { get; } = new();

    public ObservableCollection<DetectedGpuInfo> DetectedGpus { get; } = new();
    public ObservableCollection<GpuTechVendorGroupViewModel> GpuTechGroups { get; } = new();
    public ObservableCollection<GpuTechItemViewModel> SupportedGpuTechItems { get; } = new();
    public ObservableCollection<ActiveGpuBadge> ActiveGpuBadges { get; } = new();
    public ObservableCollection<string> SavedGameProfileNames { get; } = new();

    public IReadOnlyList<ColorPreset> TextColorPresets { get; }
    public IReadOnlyList<IconChoice> FluentIconChoices { get; }
    public IReadOnlyList<LanguageOption> SupportedLanguages => LocalizationService.Instance.SupportedLanguages;
    public IReadOnlyList<StretchModeOption> StretchModeOptions { get; }
    public IReadOnlyList<FontFamily> InstalledFontFamilies { get; }
    public IReadOnlyList<FontWeightOption> FontWeightOptions { get; }

    public bool IsAdministrator { get; }
    public string AdminStatusText => IsAdministrator
        ? "Đang chạy với quyền Quản trị viên"
        : "Thiếu quyền Quản trị viên (một số cảm biến & PresentMon có thể bị hạn chế)";

    public string ConfigFilePath => _configService.ConfigPath;
    public string CurrentVersionDisplay => $"v{_updateService.CurrentVersionString}";
    public string GitHubRepoDisplay => $"{UpdateService.GitHubOwner}/{UpdateService.GitHubRepo}";
    public string PresentMonPathDisplay => string.IsNullOrWhiteSpace(_presentMonService.PresentMonPath)
        ? "tools\\PresentMon.exe (chưa tìm thấy)"
        : _presentMonService.PresentMonPath;

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value != null && SetProperty(ref _selectedLanguage, value))
            {
                LocalizationService.Instance.CurrentLanguageCode = value.Code;
                OnPropertyChanged(nameof(WidgetVisibilityButtonText));
                ScheduleConfigSave();
            }
        }
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public bool IsHotkeyRegistered
    {
        get => _isHotkeyRegistered;
        set
        {
            if (SetProperty(ref _isHotkeyRegistered, value))
                OnPropertyChanged(nameof(HotkeyStatusText));
        }
    }

    public string HotkeyStatusText => IsHotkeyRegistered
        ? "Khóa Widget: Ctrl + Shift + L  •  Đặt lại FPS: Ctrl + Shift + R"
        : "Ctrl + Shift + L đang bị ứng dụng khác chiếm dụng";

    public MetricItem? SelectedCustomizationMetric
    {
        get => _selectedCustomizationMetric;
        set
        {
            if (SetProperty(ref _selectedCustomizationMetric, value))
                OnPropertyChanged(nameof(HasSelectedCustomizationMetric));
        }
    }

    public bool HasSelectedCustomizationMetric => SelectedCustomizationMetric != null;

    public bool ShowOnlyActiveInCustomization
    {
        get => _showOnlyActiveInCustomization;
        set
        {
            if (SetProperty(ref _showOnlyActiveInCustomization, value))
                RebuildFilteredCustomizationMetrics();
        }
    }

    // --- Typography Properties ---
    public FontFamily SelectedFontFamily
    {
        get => _selectedFontFamily;
        set
        {
            if (value != null && SetProperty(ref _selectedFontFamily, value))
                ScheduleConfigSave();
        }
    }

    public FontWeightOption SelectedFontWeightOption
    {
        get => _selectedFontWeightOption;
        set
        {
            if (value != null && SetProperty(ref _selectedFontWeightOption, value))
            {
                OnPropertyChanged(nameof(WidgetValueFontWeight));
                OnPropertyChanged(nameof(WidgetLabelFontWeight));
                ScheduleConfigSave();
            }
        }
    }

    public FontWeight WidgetValueFontWeight => _selectedFontWeightOption.Weight;
    public FontWeight WidgetLabelFontWeight => _selectedFontWeightOption.LabelWeight;

    // --- Auto-Scale Content Properties ---
    public bool AutoScaleContent
    {
        get => _autoScaleContent;
        set
        {
            if (SetProperty(ref _autoScaleContent, value))
            {
                RecalculateContentScale(_widgetWidth, _widgetHeight);
                ScheduleConfigSave();
            }
        }
    }

    public double ContentScaleFactor
    {
        get => _contentScaleFactor;
        private set
        {
            if (Math.Abs(_contentScaleFactor - value) > 0.004)
            {
                _contentScaleFactor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ContentScalePercentText));
                NotifyScaledMetricsChanged();
                RefreshAllMetricsInheritedTheme();
            }
        }
    }

    public string ContentScalePercentText => $"{ContentScaleFactor * 100:F0}%";

    // --- 3 Windows 11 Widget Blur Modes ---
    public WidgetBlurMode BlurMode
    {
        get => _blurMode;
        set
        {
            if (SetProperty(ref _blurMode, value))
            {
                OnPropertyChanged(nameof(IsBlurOff));
                OnPropertyChanged(nameof(IsSoftGlass));
                OnPropertyChanged(nameof(IsFrostedGlass));
                OnPropertyChanged(nameof(WidgetBackgroundBrush));
                OnPropertyChanged(nameof(WidgetBorderBrush));
                OnPropertyChanged(nameof(WidgetBorderThickness));
                OnPropertyChanged(nameof(EffectiveWidgetCornerRadius));
                OnPropertyChanged(nameof(SoftGlassSheenBrush));
                OnPropertyChanged(nameof(WidgetTextShadowOpacity));
                BlurSettingsChanged?.Invoke();
                ScheduleConfigSave();
            }
        }
    }

    public bool IsBlurOff
    {
        get => _blurMode == WidgetBlurMode.Off;
        set { if (value) BlurMode = WidgetBlurMode.Off; }
    }

    public bool IsSoftGlass
    {
        get => _blurMode == WidgetBlurMode.SoftGlass;
        set { if (value) BlurMode = WidgetBlurMode.SoftGlass; }
    }

    public bool IsFrostedGlass
    {
        get => _blurMode == WidgetBlurMode.FrostedGlass;
        set { if (value) BlurMode = WidgetBlurMode.FrostedGlass; }
    }

    /// <summary>
    /// Lớp ánh bóng khúc xạ bên trong bề mặt cho chế độ Kính mềm (Liquid Glass / Soft Glass),
    /// mô phỏng chính xác ánh sáng phản xạ mép trên và đáy của khối kính trong suốt.
    /// </summary>
    public Brush SoftGlassSheenBrush
    {
        get
        {
            var grad = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0.15, 1)
            };
            if (IsLightBackground)
            {
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(120, 255, 255, 255), 0.0));
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(24, 255, 255, 255), 0.24));
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.55));
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(46, 255, 255, 255), 1.0));
            }
            else
            {
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(72, 255, 255, 255), 0.0));
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(16, 224, 242, 254), 0.22));
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.54));
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(34, 255, 255, 255), 1.0));
            }
            grad.Freeze();
            return grad;
        }
    }

    // --- PNG Background Image Properties ---
    public string BackgroundImagePath
    {
        get => _backgroundImagePath;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (SetProperty(ref _backgroundImagePath, normalized))
            {
                RebuildBackgroundImageBrush();
                OnPropertyChanged(nameof(HasBackgroundImage));
                OnPropertyChanged(nameof(BackgroundImageDisplayName));
                ScheduleConfigSave();
            }
        }
    }

    public bool HasBackgroundImage => !string.IsNullOrWhiteSpace(_backgroundImagePath) && File.Exists(_backgroundImagePath);

    public string BackgroundImageDisplayName => HasBackgroundImage
        ? Path.GetFileName(_backgroundImagePath)
        : "Chưa chọn ảnh nền PNG";

    public double BackgroundImageOpacity
    {
        get => _backgroundImageOpacity;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 100);
            if (SetProperty(ref _backgroundImageOpacity, clamped))
            {
                RebuildBackgroundImageBrush();
                OnPropertyChanged(nameof(BackgroundImageOpacityLabel));
                ScheduleConfigSave();
            }
        }
    }

    public string BackgroundImageOpacityLabel => $"{BackgroundImageOpacity:F0}%";

    public BackgroundImageStretchMode BackgroundImageStretch
    {
        get => _backgroundImageStretch;
        set
        {
            if (SetProperty(ref _backgroundImageStretch, value))
            {
                RebuildBackgroundImageBrush();
                ScheduleConfigSave();
            }
        }
    }

    public ImageBrush? WidgetBackgroundImageBrush
    {
        get => _cachedBackgroundImageBrush;
        private set => SetProperty(ref _cachedBackgroundImageBrush, value);
    }

    // --- GPU Tech Badges & Quick Override Properties ---
    public bool UseCustomGpuBadgeColor
    {
        get => _useCustomGpuBadgeColor;
        set
        {
            if (SetProperty(ref _useCustomGpuBadgeColor, value))
            {
                OnPropertyChanged(nameof(UseVendorGpuBadgeColor));
                RebuildActiveGpuBadges();
                ScheduleConfigSave();
            }
        }
    }

    public bool UseVendorGpuBadgeColor
    {
        get => !_useCustomGpuBadgeColor;
        set { if (value) UseCustomGpuBadgeColor = false; }
    }

    public string CustomGpuBadgeColorHex
    {
        get => _customGpuBadgeColorHex;
        set
        {
            string norm = NormalizeHex(value);
            if (SetProperty(ref _customGpuBadgeColorHex, norm))
            {
                if (MetricItem.TryParseHex(norm, out _))
                {
                    OnPropertyChanged(nameof(CustomGpuBadgePreviewBrush));
                    RebuildActiveGpuBadges();
                    ScheduleConfigSave();
                }
            }
        }
    }

    public SolidColorBrush CustomGpuBadgePreviewBrush => CreateFrozenBrush(_customGpuBadgeColorHex, Color.FromRgb(96, 205, 255));

    public string GpuDetectionStatusText
    {
        get => _gpuDetectionStatusText;
        set => SetProperty(ref _gpuDetectionStatusText, value);
    }

    public bool HasActiveGpuBadges => ActiveGpuBadges.Count > 0;

    public string CurrentOverrideGameExe
    {
        get => _currentOverrideGameExe;
        set
        {
            string norm = value?.Trim() ?? string.Empty;
            if (SetProperty(ref _currentOverrideGameExe, norm))
            {
                OnPropertyChanged(nameof(HasOverrideTargetGame));
                OnPropertyChanged(nameof(OverrideTargetGameDisplay));
                SyncGameOverridesToViewModel();
                RebuildActiveGpuBadges();
            }
        }
    }

    public string ManualGameExeInput
    {
        get => _manualGameExeInput;
        set => SetProperty(ref _manualGameExeInput, value ?? string.Empty);
    }

    public bool HasOverrideTargetGame => !string.IsNullOrWhiteSpace(_currentOverrideGameExe);

    public string OverrideTargetGameDisplay => HasOverrideTargetGame
        ? $"Hồ sơ game: {_currentOverrideGameExe}"
        : "Chưa phát hiện game nào (mở game 3D hoặc nhập tên .exe để cấu hình sẵn)";

    // --- FPS Properties ---
    public bool AutoHideFpsWhenIdle
    {
        get => _autoHideFpsWhenIdle;
        set
        {
            if (SetProperty(ref _autoHideFpsWhenIdle, value))
            {
                UpdateFpsMetricsVisibility();
                ScheduleConfigSave();
            }
        }
    }

    public bool AutoHideFrameGenWhenInactive
    {
        get => _autoHideFrameGenWhenInactive;
        set
        {
            if (SetProperty(ref _autoHideFrameGenWhenInactive, value))
            {
                UpdateFpsMetricsVisibility();
                ScheduleConfigSave();
            }
        }
    }

    public double FpsGraphHeight
    {
        get => _fpsGraphHeight;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 24, 140);
            if (SetProperty(ref _fpsGraphHeight, clamped))
            {
                OnPropertyChanged(nameof(FpsGraphHeightLabel));
                OnPropertyChanged(nameof(EffectiveFpsGraphHeight));
                ScheduleConfigSave();
            }
        }
    }

    public string FpsGraphHeightLabel => $"{FpsGraphHeight:F0} px";
    public double EffectiveFpsGraphHeight => Math.Clamp(Math.Round(FpsGraphHeight * ContentScaleFactor), 22, 260);

    public string FpsGraphBaseColorHex
    {
        get => _fpsGraphBaseColorHex;
        set
        {
            string normalized = NormalizeHex(value);
            if (SetProperty(ref _fpsGraphBaseColorHex, normalized))
            {
                if (MetricItem.TryParseHex(normalized, out _))
                {
                    OnPropertyChanged(nameof(FpsGraphBaseBrush));
                    ScheduleConfigSave();
                }
            }
        }
    }

    public string FpsGraphFrameGenColorHex
    {
        get => _fpsGraphFrameGenColorHex;
        set
        {
            string normalized = NormalizeHex(value);
            if (SetProperty(ref _fpsGraphFrameGenColorHex, normalized))
            {
                if (MetricItem.TryParseHex(normalized, out _))
                {
                    OnPropertyChanged(nameof(FpsGraphFrameGenBrush));
                    ScheduleConfigSave();
                }
            }
        }
    }

    public SolidColorBrush FpsGraphBaseBrush => CreateFrozenBrush(_fpsGraphBaseColorHex, Color.FromRgb(74, 222, 128));
    public SolidColorBrush FpsGraphFrameGenBrush => CreateFrozenBrush(_fpsGraphFrameGenColorHex, Color.FromRgb(96, 205, 255));

    public string FpsStatusText
    {
        get => _fpsStatusText;
        set => SetProperty(ref _fpsStatusText, value);
    }

    public bool IsPresentMonAvailable
    {
        get => _isPresentMonAvailable;
        set
        {
            if (SetProperty(ref _isPresentMonAvailable, value))
                OnPropertyChanged(nameof(PresentMonPathDisplay));
        }
    }

    public bool IsPresentMonRunning
    {
        get => _isPresentMonRunning;
        set => SetProperty(ref _isPresentMonRunning, value);
    }

    public bool IsGameActive
    {
        get => _isGameActive;
        set => SetProperty(ref _isGameActive, value);
    }

    public bool IsDownloadingPresentMon
    {
        get => _isDownloadingPresentMon;
        set => SetProperty(ref _isDownloadingPresentMon, value);
    }

    public string PresentMonDownloadFeedback
    {
        get => _presentMonDownloadFeedback;
        set => SetProperty(ref _presentMonDownloadFeedback, value);
    }

    public IReadOnlyList<FpsGraphPoint> FpsGraphHistory
    {
        get => _fpsGraphHistory;
        private set => SetProperty(ref _fpsGraphHistory, value);
    }

    // --- System Startup & Tray ---
    public bool RunAtWindowsStartup
    {
        get => _runAtWindowsStartup;
        set
        {
            if (_runAtWindowsStartup == value) return;
            if (_isRestoring)
            {
                _runAtWindowsStartup = value;
                OnPropertyChanged();
                return;
            }

            if (Win32Helper.SetWindowsStartup(value, out string? err))
            {
                _runAtWindowsStartup = value;
                OnPropertyChanged();
                StartupStatusFeedback = value
                    ? "Đã bật khởi chạy cùng Windows (Task Scheduler mức Quản trị viên)."
                    : "Đã tắt khởi chạy cùng Windows.";
                ScheduleConfigSave();
            }
            else
            {
                StartupStatusFeedback = $"Không thể thay đổi khởi chạy cùng Windows: {err}";
                OnPropertyChanged();
            }
        }
    }

    public string StartupStatusFeedback
    {
        get => _startupStatusFeedback;
        set => SetProperty(ref _startupStatusFeedback, value);
    }

    public bool MinimizeToTrayOnClose
    {
        get => _minimizeToTrayOnClose;
        set
        {
            if (SetProperty(ref _minimizeToTrayOnClose, value))
            {
                OnPropertyChanged(nameof(CloseBehaviorFooterText));
                ScheduleConfigSave();
            }
        }
    }

    public string CloseBehaviorFooterText => MinimizeToTrayOnClose
        ? "Cấu hình tự động lưu cục bộ. Đóng cửa sổ này sẽ tự ẩn vào khay hệ thống (System Tray) để Widget tiếp tục chạy."
        : "Cấu hình tự động lưu cục bộ. Đóng cửa sổ này sẽ thoát hoàn toàn ứng dụng (đang tắt chế độ ẩn vào System Tray).";

    public bool AutoCheckUpdatesOnStartup
    {
        get => _autoCheckUpdatesOnStartup;
        set
        {
            if (SetProperty(ref _autoCheckUpdatesOnStartup, value))
                ScheduleConfigSave();
        }
    }

    public bool IsCheckingUpdates
    {
        get => _isCheckingUpdates;
        private set
        {
            if (SetProperty(ref _isCheckingUpdates, value))
                OnPropertyChanged(nameof(CanCheckUpdates));
        }
    }

    public bool CanCheckUpdates => !_isCheckingUpdates;

    public string UpdateStatusMessage
    {
        get => _updateStatusMessage;
        set => SetProperty(ref _updateStatusMessage, value);
    }

    // --- GPU Tech Vendor Filter ---
    public bool HasMultipleGpus => DetectedGpus.Count > 1;
    public bool HasDetectedGpus => DetectedGpus.Count > 0;

    public GpuTechMode GpuTechMode
    {
        get => _gpuTechMode;
        set
        {
            if (SetProperty(ref _gpuTechMode, value))
            {
                OnPropertyChanged(nameof(IsAutoAllGpusMode));
                OnPropertyChanged(nameof(IsSinglePrimaryGpuMode));
                UpdateGpuVendorSupportStates();
                ScheduleConfigSave();
            }
        }
    }

    public bool IsAutoAllGpusMode
    {
        get => _gpuTechMode == GpuTechMode.AutoAllGpus;
        set { if (value) GpuTechMode = GpuTechMode.AutoAllGpus; }
    }

    public bool IsSinglePrimaryGpuMode
    {
        get => _gpuTechMode == GpuTechMode.SinglePrimaryGpu;
        set { if (value) GpuTechMode = GpuTechMode.SinglePrimaryGpu; }
    }

    public DetectedGpuInfo? SelectedPrimaryGpu
    {
        get => _selectedPrimaryGpu;
        set
        {
            if (SetProperty(ref _selectedPrimaryGpu, value))
            {
                UpdateGpuVendorSupportStates();
                ScheduleConfigSave();
            }
        }
    }

    public bool HideUnsupportedGpuTechs
    {
        get => _hideUnsupportedGpuTechs;
        set
        {
            if (SetProperty(ref _hideUnsupportedGpuTechs, value))
            {
                UpdateGpuVendorSupportStates();
                ScheduleConfigSave();
            }
        }
    }

    // --- Widget Bounds & Appearance ---
    public double WidgetLeft
    {
        get => _widgetLeft;
        set { if (SetProperty(ref _widgetLeft, value)) ScheduleConfigSave(); }
    }

    public double WidgetTop
    {
        get => _widgetTop;
        set { if (SetProperty(ref _widgetTop, value)) ScheduleConfigSave(); }
    }

    public double WidgetWidth
    {
        get => _widgetWidth;
        set
        {
            double clamped = Math.Clamp(value, 150, 2400);
            if (SetProperty(ref _widgetWidth, clamped))
            {
                RecalculateContentScale(_widgetWidth, _widgetHeight);
                ScheduleConfigSave();
            }
        }
    }

    public double WidgetHeight
    {
        get => _widgetHeight;
        set
        {
            double clamped = Math.Clamp(value, 42, 1600);
            if (SetProperty(ref _widgetHeight, clamped))
            {
                RecalculateContentScale(_widgetWidth, _widgetHeight);
                ScheduleConfigSave();
            }
        }
    }

    public double BackgroundOpacity
    {
        get => _backgroundOpacity;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 100);
            if (SetProperty(ref _backgroundOpacity, clamped))
            {
                OnPropertyChanged(nameof(BackgroundOpacityLabel));
                OnPropertyChanged(nameof(WidgetBackgroundBrush));
                OnPropertyChanged(nameof(WidgetBorderBrush));
                OnPropertyChanged(nameof(WidgetTextShadowOpacity));
                BlurSettingsChanged?.Invoke();
                ScheduleConfigSave();
            }
        }
    }

    public string BackgroundOpacityLabel => BackgroundOpacity <= 0
        ? "0% (Trong suốt hoàn toàn)"
        : $"{BackgroundOpacity:F0}%";

    public string BackgroundColorHex
    {
        get => _backgroundColorHex;
        set
        {
            if (MetricItem.TryParseHex(value, out _))
            {
                string normalized = value.Trim().ToUpperInvariant();
                if (SetProperty(ref _backgroundColorHex, normalized))
                {
                    OnPropertyChanged(nameof(IsDarkBackground));
                    OnPropertyChanged(nameof(IsLightBackground));
                    OnPropertyChanged(nameof(WidgetBackgroundBrush));
                    OnPropertyChanged(nameof(WidgetBorderBrush));
                    OnPropertyChanged(nameof(SoftGlassSheenBrush));
                    RefreshAllMetricsInheritedTheme();
                    BlurSettingsChanged?.Invoke();
                    ScheduleConfigSave();
                }
            }
        }
    }

    public bool IsDarkBackground
    {
        get => !IsLightBackground;
        set
        {
            if (value)
            {
                BackgroundColorHex = "#141419";
                if (string.Equals(TextColorHex, "#141418", StringComparison.OrdinalIgnoreCase))
                    TextColorHex = "#FFFFFF";
            }
        }
    }

    public bool IsLightBackground
    {
        get => string.Equals(_backgroundColorHex, "#F5F7FA", StringComparison.OrdinalIgnoreCase);
        set
        {
            if (value)
            {
                BackgroundColorHex = "#F5F7FA";
                if (string.Equals(TextColorHex, "#FFFFFF", StringComparison.OrdinalIgnoreCase))
                    TextColorHex = "#141418";
            }
        }
    }

    public string TextColorHex
    {
        get => _textColorHex;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            string normalized = NormalizeHex(value);
            if (SetProperty(ref _textColorHex, normalized))
            {
                if (MetricItem.TryParseHex(normalized, out _))
                {
                    OnPropertyChanged(nameof(WidgetTextBrush));
                    OnPropertyChanged(nameof(WidgetSecondaryTextBrush));
                    RefreshAllMetricsInheritedTheme();
                    ScheduleConfigSave();
                }
            }
        }
    }

    public double FontSize
    {
        get => _fontSize;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 10, 36);
            if (SetProperty(ref _fontSize, clamped))
            {
                OnPropertyChanged(nameof(FontSizeLabel));
                OnPropertyChanged(nameof(WidgetLabelFontSize));
                OnPropertyChanged(nameof(WidgetIconFontSize));
                OnPropertyChanged(nameof(WidgetBadgeFontSize));
                NotifyScaledMetricsChanged();
                ScheduleConfigSave();
            }
        }
    }

    public string FontSizeLabel => $"{FontSize:F0} px";
    public double WidgetLabelFontSize => Math.Max(9, Math.Round(FontSize * 0.85, 1));
    public double WidgetIconFontSize => Math.Max(12, Math.Round(FontSize * 1.12, 1));
    public double WidgetBadgeFontSize => Math.Max(9.5, Math.Round(FontSize * 0.80, 1));

    // Các kích thước đã áp dụng hệ số tỷ lệ mượt theo kích thước cửa sổ Widget (vector sắc nét, không nhòe)
    public double EffectiveFontSize => Math.Clamp(Math.Round(FontSize * ContentScaleFactor, 1), 9, 64);
    public double EffectiveLabelFontSize => Math.Clamp(Math.Round(WidgetLabelFontSize * ContentScaleFactor, 1), 8.5, 54);
    public double EffectiveIconFontSize => Math.Clamp(Math.Round(WidgetIconFontSize * ContentScaleFactor, 1), 11, 68);
    public double EffectiveBadgeFontSize => Math.Clamp(Math.Round(WidgetBadgeFontSize * ContentScaleFactor, 1), 8.5, 42);
    public double EffectiveHorizontalSpacing => Math.Clamp(Math.Round(12 * ContentScaleFactor, 1), 6, 32);
    public double EffectiveVerticalSpacing => Math.Clamp(Math.Round(6 * ContentScaleFactor, 1), 3, 20);
    public Thickness EffectiveWidgetPadding => new(
        Math.Clamp(Math.Round(12 * ContentScaleFactor), 6, 30),
        Math.Clamp(Math.Round(10 * ContentScaleFactor), 5, 24),
        Math.Clamp(Math.Round(12 * ContentScaleFactor), 6, 30),
        Math.Clamp(Math.Round(10 * ContentScaleFactor), 5, 24));
    public double EffectiveGraphMinWidth => Math.Clamp(Math.Round(156 * ContentScaleFactor), 120, 420);

    public double CornerRadius
    {
        get => _cornerRadius;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 28);
            if (SetProperty(ref _cornerRadius, clamped))
            {
                OnPropertyChanged(nameof(CornerRadiusLabel));
                OnPropertyChanged(nameof(WidgetCornerRadius));
                OnPropertyChanged(nameof(EffectiveWidgetCornerRadius));
                BlurSettingsChanged?.Invoke();
                ScheduleConfigSave();
            }
        }
    }

    public string CornerRadiusLabel => $"{CornerRadius:F0} px";
    public CornerRadius WidgetCornerRadius => new(CornerRadius);

    /// <summary>
    /// Khi bật Kính mềm / Kính mờ, khớp khít bán kính góc của DWM Windows 11 (0 / 4 / 8 px)
    /// để lớp làm mờ ôm sát viền bo tròn, không hở 4 góc vuông đen.
    /// </summary>
    public CornerRadius EffectiveWidgetCornerRadius
    {
        get
        {
            if (BlurMode == WidgetBlurMode.Off)
                return new CornerRadius(CornerRadius);

            if (CornerRadius <= 0.5) return new CornerRadius(0);
            if (CornerRadius <= 6.0) return new CornerRadius(4);
            return new CornerRadius(8);
        }
    }

    public bool IsClickThrough
    {
        get => _isClickThrough;
        set
        {
            if (SetProperty(ref _isClickThrough, value))
            {
                OnPropertyChanged(nameof(ClickThroughStatusLabel));
                OnPropertyChanged(nameof(IsUnlocked));
                ClickThroughChanged?.Invoke(value);
                ScheduleConfigSave();
            }
        }
    }

    public bool IsUnlocked => !IsClickThrough;

    public string ClickThroughStatusLabel => IsClickThrough
        ? "Đang khóa (Xuyên chuột)"
        : "Đang mở khóa (Có thể kéo & co giãn)";

    public bool IsWidgetTopmost
    {
        get => _isWidgetTopmost;
        set
        {
            if (SetProperty(ref _isWidgetTopmost, value))
            {
                WidgetTopmostChanged?.Invoke(value);
                ScheduleConfigSave();
            }
        }
    }

    public bool IsWidgetVisible
    {
        get => _isWidgetVisible;
        set
        {
            if (SetProperty(ref _isWidgetVisible, value))
            {
                OnPropertyChanged(nameof(WidgetVisibilityButtonText));
                WidgetVisibilityChanged?.Invoke(value);
                ScheduleConfigSave();
            }
        }
    }

    public string WidgetVisibilityButtonText => IsWidgetVisible
        ? LocalizationService.Instance["BtnHideWidget"]
        : LocalizationService.Instance["BtnShowWidget"];

    public SolidColorBrush WidgetBackgroundBrush
    {
        get
        {
            var baseColor = MetricItem.TryParseHex(BackgroundColorHex, out var c) ? c : Color.FromRgb(20, 20, 25);

            double opacityFactor = BlurMode switch
            {
                // Kính mềm: giữ trong trẻo tối đa ở 0%, tăng nhẹ sắc nền khi kéo thanh trượt
                WidgetBlurMode.SoftGlass => (BackgroundOpacity / 100.0) * 0.24,
                // Kính mờ: DWM Acrylic đã phủ độ mờ chính, chỉ thêm lớp tint rất nhẹ bổ trợ
                WidgetBlurMode.FrostedGlass => (BackgroundOpacity / 100.0) * 0.22,
                // Tắt làm mờ: 0% -> trong suốt hoàn toàn (alpha = 0), 100% -> đặc hoàn toàn
                _ => BackgroundOpacity / 100.0
            };

            byte alpha = (byte)Math.Clamp(Math.Round(opacityFactor * 255.0), 0, 255);
            var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
            brush.Freeze();
            return brush;
        }
    }

    public Brush WidgetBorderBrush
    {
        get
        {
            if (BlurMode == WidgetBlurMode.SoftGlass)
            {
                // Kính mềm (Liquid Glass): viền phản quang trắng sáng sắc nét giống mẫu
                var grad = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0.18, 1)
                };
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(245, 255, 255, 255), 0.0));
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(72, 235, 248, 255), 0.30));
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(42, 255, 255, 255), 0.65));
                grad.GradientStops.Add(new GradientStop(Color.FromArgb(205, 255, 255, 255), 1.0));
                grad.Freeze();
                return grad;
            }

            if (BlurMode == WidgetBlurMode.FrostedGlass)
            {
                // Kính mờ (Frosted Glass): viền cực mảnh mềm dịu giống mẫu kính phun cát
                var grad = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1)
                };
                if (IsLightBackground)
                {
                    grad.GradientStops.Add(new GradientStop(Color.FromArgb(85, 255, 255, 255), 0.0));
                    grad.GradientStops.Add(new GradientStop(Color.FromArgb(30, 15, 23, 42), 1.0));
                }
                else
                {
                    grad.GradientStops.Add(new GradientStop(Color.FromArgb(76, 255, 255, 255), 0.0));
                    grad.GradientStops.Add(new GradientStop(Color.FromArgb(24, 255, 255, 255), 1.0));
                }
                grad.Freeze();
                return grad;
            }

            if (BackgroundOpacity < 6 && !HasBackgroundImage)
            {
                var transparent = new SolidColorBrush(Colors.Transparent);
                transparent.Freeze();
                return transparent;
            }

            byte alpha = (byte)Math.Clamp(Math.Round(Math.Max(BackgroundOpacity, HasBackgroundImage ? 45 : 0) / 100.0 * 52.0), 10, 56);
            Color borderColor = IsLightBackground
                ? Color.FromArgb(alpha, 0, 0, 0)
                : Color.FromArgb(alpha, 255, 255, 255);
            var brush = new SolidColorBrush(borderColor);
            brush.Freeze();
            return brush;
        }
    }

    public Thickness WidgetBorderThickness => BlurMode == WidgetBlurMode.SoftGlass
        ? new Thickness(1.35)
        : new Thickness(1);

    public SolidColorBrush WidgetTextBrush => CreateFrozenBrush(TextColorHex, Colors.White);

    public SolidColorBrush WidgetSecondaryTextBrush
    {
        get
        {
            var c = MetricItem.TryParseHex(TextColorHex, out var parsed) ? parsed : Colors.White;
            var brush = new SolidColorBrush(Color.FromArgb(196, c.R, c.G, c.B));
            brush.Freeze();
            return brush;
        }
    }

    public double WidgetTextShadowOpacity =>
        (BlurMode == WidgetBlurMode.SoftGlass || (BackgroundOpacity < 45 && !HasBackgroundImage))
            ? 0.68
            : 0.28;

    public bool HasActiveMetrics => ActiveMetrics.Count > 0;
    public string ActiveMetricCountText => $"{ActiveMetrics.Count} mục";

    public event Action<bool>? ClickThroughChanged;
    public event Action<bool>? WidgetTopmostChanged;
    public event Action<bool>? WidgetVisibilityChanged;
    public event Action? CenterWidgetRequested;
    public event Action? OpenSettingsRequested;
    public event Action? ExitAppRequested;
    public event Action? ResetFpsToastRequested;
    public event Action? BlurSettingsChanged;
    public event Action<UpdateReleaseInfo>? UpdateAvailableDetected;

    public ICommand ToggleMetricCommand { get; }
    public ICommand RemoveMetricCommand { get; }
    public ICommand CustomizeMetricCommand { get; }
    public ICommand SelectColorPresetCommand { get; }
    public ICommand ToggleWidgetVisibilityCommand { get; }
    public ICommand CenterWidgetCommand { get; }
    public ICommand ResetDefaultsCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ToggleClickThroughCommand { get; }
    public ICommand ToggleWidgetTopmostCommand { get; }
    public ICommand ExitAppCommand { get; }

    public ICommand ApplyHeaderStyleAllCommand { get; }
    public ICommand HighlightAllMetricsCommand { get; }
    public ICommand ApplySelectedHighlightIntensityAllCommand { get; }
    public ICommand ResetSelectedMetricLabelCommand { get; }
    public ICommand ClearSelectedLabelColorCommand { get; }
    public ICommand ClearSelectedValueColorCommand { get; }
    public ICommand SelectMetricLabelColorCommand { get; }
    public ICommand SelectMetricValueColorCommand { get; }
    public ICommand SelectMetricIconCommand { get; }
    public ICommand ResetSelectedMetricCommand { get; }
    public ICommand ResetAllMetricStylesCommand { get; }

    public ICommand ResetFpsStatsCommand { get; }
    public ICommand DownloadPresentMonCommand { get; }
    public ICommand SelectFpsGraphBaseColorCommand { get; }
    public ICommand SelectFpsGraphFrameGenColorCommand { get; }

    public ICommand CycleTechOverrideCommand { get; }
    public ICommand ApplyManualGameExeCommand { get; }
    public ICommand ClearCurrentGameOverridesCommand { get; }
    public ICommand SelectCustomGpuBadgeColorCommand { get; }

    public ICommand ChooseBackgroundImageCommand { get; }
    public ICommand ClearBackgroundImageCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }

    public MainViewModel(
        ConfigService configService,
        HardwareMonitorService hardwareService,
        PresentMonService presentMonService,
        GpuTechDetectorService gpuTechDetectorService,
        UpdateService updateService)
    {
        _configService = configService;
        _hardwareService = hardwareService;
        _presentMonService = presentMonService;
        _gpuTechDetectorService = gpuTechDetectorService;
        _updateService = updateService;
        IsAdministrator = Win32Helper.IsRunningAsAdministrator();
        _selectedLanguage = LocalizationService.Instance.SupportedLanguages[0];

        // Nạp toàn bộ phông chữ đang cài đặt trên Windows, sắp xếp theo bảng chữ cái
        InstalledFontFamilies = Fonts.SystemFontFamilies
            .OrderBy(f => f.Source, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        FontWeightOptions = new List<FontWeightOption>
        {
            new("Thin", "Rất mảnh (Thin)", FontWeights.Thin, FontWeights.Thin),
            new("Light", "Mảnh (Light)", FontWeights.Light, FontWeights.Light),
            new("Normal", "Thường (Normal)", FontWeights.Normal, FontWeights.Light),
            new("Medium", "Vừa (Medium)", FontWeights.Medium, FontWeights.Normal),
            new("SemiBold", "Hơi đậm (SemiBold)", FontWeights.SemiBold, FontWeights.Medium),
            new("Bold", "Đậm (Bold)", FontWeights.Bold, FontWeights.Medium),
            new("ExtraBold", "Rất đậm (ExtraBold)", FontWeights.ExtraBold, FontWeights.SemiBold)
        };
        _selectedFontWeightOption = FontWeightOptions[4]; // SemiBold

        StretchModeOptions = new List<StretchModeOption>
        {
            new(BackgroundImageStretchMode.UniformToFill, "Lấp đầy khung (UniformToFill)"),
            new(BackgroundImageStretchMode.Uniform, "Giữ tỷ lệ vừa khung (Uniform)"),
            new(BackgroundImageStretchMode.Fill, "Kéo giãn vừa khít (Fill)")
        };

        TextColorPresets = new List<ColorPreset>
        {
            MakePreset("Trắng", "#FFFFFF"),
            MakePreset("Bạc sáng", "#E2E8F0"),
            MakePreset("Đen than", "#141418"),
            MakePreset("Xanh Win11", "#60CDFF"),
            MakePreset("Xanh ngọc", "#2DD4BF"),
            MakePreset("Xanh lá", "#4ADE80"),
            MakePreset("NVIDIA Green", "#76B900"),
            MakePreset("Vàng nắng", "#FACC15"),
            MakePreset("Cam ấm", "#FB923C"),
            MakePreset("Đỏ san hô", "#F87171"),
            MakePreset("Tím nhạt", "#C084FC")
        };

        FluentIconChoices = new List<IconChoice>
        {
            new() { Name = "Chip CPU", SymbolName = nameof(SymbolRegular.DeveloperBoard24), Symbol = SymbolRegular.DeveloperBoard24 },
            new() { Name = "Nhiệt kế", SymbolName = nameof(SymbolRegular.Temperature24), Symbol = SymbolRegular.Temperature24 },
            new() { Name = "Xung nhịp / FrameGen", SymbolName = nameof(SymbolRegular.TopSpeed24), Symbol = SymbolRegular.TopSpeed24 },
            new() { Name = "Công suất", SymbolName = nameof(SymbolRegular.Flash24), Symbol = SymbolRegular.Flash24 },
            new() { Name = "Đồ họa / FPS", SymbolName = nameof(SymbolRegular.Games24), Symbol = SymbolRegular.Games24 },
            new() { Name = "Đồ thị FPS", SymbolName = nameof(SymbolRegular.ChartMultiple24), Symbol = SymbolRegular.ChartMultiple24 },
            new() { Name = "Màn hình / VRAM", SymbolName = nameof(SymbolRegular.DesktopPulse24), Symbol = SymbolRegular.DesktopPulse24 },
            new() { Name = "Đồng hồ đo / Avg", SymbolName = nameof(SymbolRegular.Gauge24), Symbol = SymbolRegular.Gauge24 },
            new() { Name = "Thanh RAM", SymbolName = nameof(SymbolRegular.Ram20), Symbol = SymbolRegular.Ram20 },
            new() { Name = "Bộ nhớ", SymbolName = nameof(SymbolRegular.Memory16), Symbol = SymbolRegular.Memory16 },
            new() { Name = "Ổ cứng (Đọc)", SymbolName = nameof(SymbolRegular.HardDrive24), Symbol = SymbolRegular.HardDrive24 },
            new() { Name = "Lưu trữ (Ghi)", SymbolName = nameof(SymbolRegular.Storage24), Symbol = SymbolRegular.Storage24 },
            new() { Name = "Tải xuống", SymbolName = nameof(SymbolRegular.ArrowDownload28), Symbol = SymbolRegular.ArrowDownload28 },
            new() { Name = "Tải lên", SymbolName = nameof(SymbolRegular.ArrowUpload24), Symbol = SymbolRegular.ArrowUpload24 },
            new() { Name = "Mạng", SymbolName = nameof(SymbolRegular.Globe28), Symbol = SymbolRegular.Globe28 },
            new() { Name = "Lưu lượng / 1% Low", SymbolName = nameof(SymbolRegular.DataUsage24), Symbol = SymbolRegular.DataUsage24 },
            new() { Name = "Thời gian / 0.1% Low", SymbolName = nameof(SymbolRegular.ClockAlarm24), Symbol = SymbolRegular.ClockAlarm24 },
            new() { Name = "Hệ thống", SymbolName = nameof(SymbolRegular.Server24), Symbol = SymbolRegular.Server24 }
        };

        ToggleMetricCommand = new RelayCommand(p =>
        {
            if (p is MetricItem item) ToggleMetric(item);
            else if (p is string id && _metricsById.TryGetValue(id, out var found)) ToggleMetric(found);
        });

        RemoveMetricCommand = new RelayCommand(p =>
        {
            if (p is MetricItem item) RemoveMetric(item);
            else if (p is string id && _metricsById.TryGetValue(id, out var found)) RemoveMetric(found);
        });

        CustomizeMetricCommand = new RelayCommand(p =>
        {
            if (p is MetricItem item)
            {
                if (ShowOnlyActiveInCustomization && !item.IsSelected)
                    ShowOnlyActiveInCustomization = false;
                SelectedCustomizationMetric = item;
                SelectedTabIndex = 1;
            }
        });

        SelectColorPresetCommand = new RelayCommand(p => { if (p is string hex) TextColorHex = hex; });

        ApplyHeaderStyleAllCommand = new RelayCommand(p =>
        {
            if (p is string mode)
            {
                var style = string.Equals(mode, "icon", StringComparison.OrdinalIgnoreCase)
                    ? MetricHeaderStyle.Icon
                    : MetricHeaderStyle.Text;
                foreach (var m in AllMetrics)
                    m.HeaderStyle = style;
                ScheduleConfigSave();
            }
        });

        HighlightAllMetricsCommand = new RelayCommand(p =>
        {
            bool enable = string.Equals(p as string, "on", StringComparison.OrdinalIgnoreCase);
            foreach (var m in AllMetrics)
                m.IsHighlightedBlock = enable;
            ScheduleConfigSave();
        });

        ApplySelectedHighlightIntensityAllCommand = new RelayCommand(() =>
        {
            if (SelectedCustomizationMetric == null) return;
            double level = SelectedCustomizationMetric.HighlightIntensity;
            foreach (var m in AllMetrics)
            {
                m.HighlightIntensity = level;
                if (SelectedCustomizationMetric.IsHighlightedBlock)
                    m.IsHighlightedBlock = true;
            }
            ScheduleConfigSave();
        });

        ResetSelectedMetricLabelCommand = new RelayCommand(() =>
        {
            if (SelectedCustomizationMetric != null)
                SelectedCustomizationMetric.CustomLabel = string.Empty;
        });

        ClearSelectedLabelColorCommand = new RelayCommand(() =>
        {
            if (SelectedCustomizationMetric != null)
                SelectedCustomizationMetric.CustomLabelColorHex = string.Empty;
        });

        ClearSelectedValueColorCommand = new RelayCommand(() =>
        {
            if (SelectedCustomizationMetric != null)
                SelectedCustomizationMetric.CustomValueColorHex = string.Empty;
        });

        SelectMetricLabelColorCommand = new RelayCommand(p =>
        {
            if (SelectedCustomizationMetric != null && p is string hex)
                SelectedCustomizationMetric.CustomLabelColorHex = hex;
        });

        SelectMetricValueColorCommand = new RelayCommand(p =>
        {
            if (SelectedCustomizationMetric != null && p is string hex)
                SelectedCustomizationMetric.CustomValueColorHex = hex;
        });

        SelectMetricIconCommand = new RelayCommand(p =>
        {
            if (SelectedCustomizationMetric != null && p is string symbolName)
            {
                SelectedCustomizationMetric.CustomSymbolName = symbolName;
                if (SelectedCustomizationMetric.HeaderStyle != MetricHeaderStyle.Icon)
                    SelectedCustomizationMetric.HeaderStyle = MetricHeaderStyle.Icon;
            }
        });

        ResetSelectedMetricCommand = new RelayCommand(() => SelectedCustomizationMetric?.ResetCustomizations());

        ResetAllMetricStylesCommand = new RelayCommand(() =>
        {
            foreach (var m in AllMetrics)
                m.ResetCustomizations();
            ScheduleConfigSave();
        });

        ResetFpsStatsCommand = new RelayCommand(ResetFpsStats);

        DownloadPresentMonCommand = new RelayCommand(async () =>
        {
            if (IsDownloadingPresentMon) return;
            IsDownloadingPresentMon = true;
            PresentMonDownloadFeedback = "Đang tải Intel PresentMon 2.x từ GitHub...";
            try
            {
                var (ok, msg) = await _presentMonService.DownloadOfficialPresentMonAsync();
                PresentMonDownloadFeedback = msg;
                IsPresentMonAvailable = _presentMonService.IsPresentMonAvailable;
                IsPresentMonRunning = _presentMonService.IsRunning;
                OnPropertyChanged(nameof(PresentMonPathDisplay));
            }
            finally
            {
                IsDownloadingPresentMon = false;
            }
        });

        SelectFpsGraphBaseColorCommand = new RelayCommand(p => { if (p is string hex) FpsGraphBaseColorHex = hex; });
        SelectFpsGraphFrameGenColorCommand = new RelayCommand(p => { if (p is string hex) FpsGraphFrameGenColorHex = hex; });

        CycleTechOverrideCommand = new RelayCommand(p =>
        {
            if (p is GpuTechItemViewModel tech && HasOverrideTargetGame)
            {
                tech.GameOverride = tech.GameOverride switch
                {
                    GameTechOverrideState.Auto => GameTechOverrideState.ForceOn,
                    GameTechOverrideState.ForceOn => GameTechOverrideState.ForceOff,
                    _ => GameTechOverrideState.Auto
                };
            }
        });

        ApplyManualGameExeCommand = new RelayCommand(() =>
        {
            string raw = ManualGameExeInput.Trim();
            if (string.IsNullOrWhiteSpace(raw)) return;
            if (!raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                raw += ".exe";
            EnsureGameProfileExists(raw);
            CurrentOverrideGameExe = raw;
            ManualGameExeInput = string.Empty;
        });

        ClearCurrentGameOverridesCommand = new RelayCommand(() =>
        {
            if (!HasOverrideTargetGame) return;
            _gameTechProfiles.Remove(CurrentOverrideGameExe);
            RebuildSavedGameProfileNames();
            SyncGameOverridesToViewModel();
            RebuildActiveGpuBadges();
            ScheduleConfigSave();
        });

        SelectCustomGpuBadgeColorCommand = new RelayCommand(p =>
        {
            if (p is string hex)
            {
                CustomGpuBadgeColorHex = hex;
                UseCustomGpuBadgeColor = true;
            }
        });

        ChooseBackgroundImageCommand = new RelayCommand(() =>
        {
            var dlg = new OpenFileDialog
            {
                Title = "Chọn ảnh nền PNG cho Widget",
                Filter = "Ảnh PNG (*.png)|*.png|Tất cả ảnh (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp"
            };
            if (dlg.ShowDialog() == true)
                BackgroundImagePath = dlg.FileName;
        });

        ClearBackgroundImageCommand = new RelayCommand(() => BackgroundImagePath = string.Empty);
        CheckForUpdatesCommand = new RelayCommand(async () => await CheckForUpdatesManualAsync());

        ToggleWidgetVisibilityCommand = new RelayCommand(() => IsWidgetVisible = !IsWidgetVisible);
        CenterWidgetCommand = new RelayCommand(() => CenterWidgetRequested?.Invoke());
        ResetDefaultsCommand = new RelayCommand(ResetToDefaults);
        OpenSettingsCommand = new RelayCommand(() => OpenSettingsRequested?.Invoke());
        ToggleClickThroughCommand = new RelayCommand(() => IsClickThrough = !IsClickThrough);
        ToggleWidgetTopmostCommand = new RelayCommand(() => IsWidgetTopmost = !IsWidgetTopmost);
        ExitAppCommand = new RelayCommand(() => ExitAppRequested?.Invoke());

        ActiveMetrics.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasActiveMetrics));
            OnPropertyChanged(nameof(ActiveMetricCountText));
            if (ShowOnlyActiveInCustomization)
                RebuildFilteredCustomizationMetrics();
        };

        PopulateMetricsFromService();
        PopulateGpuTechnologies();
        LoadFromConfig();
        RecalculateContentScale(_widgetWidth, _widgetHeight);
        RefreshAllMetricsInheritedTheme();
        RebuildFilteredCustomizationMetrics();
        UpdateGpuVendorSupportStates();
        SyncGameOverridesToViewModel();
        RebuildActiveGpuBadges();
        RebuildBackgroundImageBrush();
        UpdateFpsMetricsVisibility();
    }

    /// <summary>
    /// Cập nhật hệ số co giãn nội dung mượt mà theo kích thước cửa sổ Widget trong lúc người dùng kéo cạnh/góc.
    /// </summary>
    public void UpdateLiveWidgetSize(double width, double height)
    {
        RecalculateContentScale(width, height);
    }

    private void RecalculateContentScale(double width, double height)
    {
        if (!AutoScaleContent || width <= 0 || height <= 0)
        {
            ContentScaleFactor = 1.0;
            return;
        }

        double areaRatio = Math.Sqrt((width * height) / (ScaleBaselineWidth * ScaleBaselineHeight));

        // Đảm bảo khi kéo thành thanh ngang siêu mỏng thì chữ không cao vượt khung gây cắt chữ
        double maxByHeight = Math.Max(0.76, (height - 14.0) / Math.Max(22.0, FontSize * 2.0));
        double maxByWidth = Math.Max(0.76, width / 180.0);

        double raw = Math.Min(areaRatio, Math.Min(maxByHeight, maxByWidth));
        ContentScaleFactor = Math.Clamp(Math.Round(raw, 3), 0.76, 2.50);
    }

    private void NotifyScaledMetricsChanged()
    {
        OnPropertyChanged(nameof(EffectiveFontSize));
        OnPropertyChanged(nameof(EffectiveLabelFontSize));
        OnPropertyChanged(nameof(EffectiveIconFontSize));
        OnPropertyChanged(nameof(EffectiveBadgeFontSize));
        OnPropertyChanged(nameof(EffectiveFpsGraphHeight));
        OnPropertyChanged(nameof(EffectiveHorizontalSpacing));
        OnPropertyChanged(nameof(EffectiveVerticalSpacing));
        OnPropertyChanged(nameof(EffectiveWidgetPadding));
        OnPropertyChanged(nameof(EffectiveGraphMinWidth));
    }

    public async System.Threading.Tasks.Task CheckForUpdatesManualAsync()
    {
        if (IsCheckingUpdates) return;
        IsCheckingUpdates = true;
        UpdateStatusMessage = "Đang kiểm tra bản cập nhật mới từ GitHub...";
        try
        {
            var result = await _updateService.CheckForUpdatesAsync();
            UpdateStatusMessage = result.StatusMessage;
            if (result.HasUpdate && result.Release != null)
            {
                UpdateAvailableDetected?.Invoke(result.Release);
            }
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    public async System.Threading.Tasks.Task CheckForUpdatesSilentAsync()
    {
        try
        {
            var result = await _updateService.CheckForUpdatesAsync();
            if (result.HasUpdate && result.Release != null)
            {
                UpdateStatusMessage = result.StatusMessage;
                UpdateAvailableDetected?.Invoke(result.Release);
            }
        }
        catch
        {
            // Bỏ qua nhẹ nhàng nếu mất mạng hoặc chưa có bản phát hành trên GitHub
        }
    }

    public void ResetFpsStats()
    {
        _presentMonService.ResetStats();
        FpsGraphHistory = Array.Empty<FpsGraphPoint>();
        ResetFpsToastRequested?.Invoke();
    }

    public void ApplyFpsSnapshot(FpsSnapshot snap)
    {
        IsPresentMonAvailable = snap.IsPresentMonAvailable;
        IsPresentMonRunning = snap.IsPresentMonRunning;
        FpsStatusText = snap.StatusText;
        IsGameActive = snap.IsGameActive;
        _isFrameGenActive = snap.FrameGenFps.HasValue;

        _gpuTechDetectorService.UpdatePresentMonHint(snap.IsGameActive, snap.ActiveGameName, snap.FrameGenSource);

        SetMetricValue("fps_base", snap.BaseFps.HasValue ? $"{snap.BaseFps.Value:F0} FPS" : "--");
        SetMetricValue("fps_framegen", snap.FrameGenFps.HasValue ? $"{snap.FrameGenFps.Value:F0} FPS" : "--");
        SetMetricValue("fps_avg", snap.AvgFps.HasValue ? $"{snap.AvgFps.Value:F0} FPS" : "--");
        SetMetricValue("fps_1low", snap.OnePercentLowFps.HasValue ? $"{snap.OnePercentLowFps.Value:F0} FPS" : "--");
        SetMetricValue("fps_01low", snap.PointOnePercentLowFps.HasValue ? $"{snap.PointOnePercentLowFps.Value:F0} FPS" : "--");
        SetMetricValue("fps_graph", snap.BaseFps.HasValue
            ? (snap.FrameGenFps.HasValue
                ? $"{snap.BaseFps.Value:F0} → {snap.FrameGenFps.Value:F0}"
                : $"{snap.BaseFps.Value:F0} FPS")
            : "--");

        UpdateFpsMetricsVisibility();
        FpsGraphHistory = snap.GraphHistory;
    }

    public void ApplyGpuTechDetection(GpuTechDetectionSnapshot snap)
    {
        GpuDetectionStatusText = snap.DetectionModeNote;
        _isGpuTechGameActive = snap.IsGameActive;
        _lastAutoDetectedTechIds = new HashSet<string>(snap.DetectedTechIds, StringComparer.OrdinalIgnoreCase);

        if (snap.IsGameActive && !string.IsNullOrWhiteSpace(snap.ForegroundExeName))
        {
            if (!string.Equals(_currentOverrideGameExe, snap.ForegroundExeName, StringComparison.OrdinalIgnoreCase))
                CurrentOverrideGameExe = snap.ForegroundExeName;
        }

        foreach (var group in GpuTechGroups)
            foreach (var tech in group.Items)
                tech.IsAutoDetected = snap.IsGameActive && _lastAutoDetectedTechIds.Contains(tech.Id);

        RebuildActiveGpuBadges();
        UpdateFpsMetricsVisibility();
    }

    private void OnTechOverrideChanged(GpuTechItemViewModel tech)
    {
        if (_isRestoring || !HasOverrideTargetGame) return;

        if (!_gameTechProfiles.TryGetValue(CurrentOverrideGameExe, out var map))
        {
            map = new Dictionary<string, GameTechOverrideState>(StringComparer.OrdinalIgnoreCase);
            _gameTechProfiles[CurrentOverrideGameExe] = map;
        }

        if (tech.GameOverride == GameTechOverrideState.Auto) map.Remove(tech.Id);
        else map[tech.Id] = tech.GameOverride;

        if (map.Count == 0) _gameTechProfiles.Remove(CurrentOverrideGameExe);

        RebuildSavedGameProfileNames();
        RebuildActiveGpuBadges();
        UpdateFpsMetricsVisibility();
        ScheduleConfigSave();
    }

    private void SyncGameOverridesToViewModel()
    {
        Dictionary<string, GameTechOverrideState>? map = null;
        if (HasOverrideTargetGame)
            _gameTechProfiles.TryGetValue(CurrentOverrideGameExe, out map);

        foreach (var group in GpuTechGroups)
        {
            foreach (var tech in group.Items)
            {
                var state = map != null && map.TryGetValue(tech.Id, out var saved)
                    ? saved
                    : GameTechOverrideState.Auto;
                tech.SetOverrideSilent(state);
            }
        }
    }

    private void RebuildActiveGpuBadges()
    {
        ActiveGpuBadges.Clear();

        Dictionary<string, GameTechOverrideState>? gameMap = null;
        if (HasOverrideTargetGame)
            _gameTechProfiles.TryGetValue(CurrentOverrideGameExe, out gameMap);

        foreach (var group in GpuTechGroups)
        {
            if (!group.IsSupported) continue;
            foreach (var tech in group.Items)
            {
                if (!tech.IsEnabled) continue;

                var ov = gameMap != null && gameMap.TryGetValue(tech.Id, out var s)
                    ? s
                    : GameTechOverrideState.Auto;

                bool show = ov switch
                {
                    GameTechOverrideState.ForceOn => true,
                    GameTechOverrideState.ForceOff => false,
                    _ => _isGpuTechGameActive && _lastAutoDetectedTechIds.Contains(tech.Id)
                };

                if (!show) continue;

                Color baseColor = GetBadgeColor(tech.Vendor);
                var bg = new SolidColorBrush(Color.FromArgb(46, baseColor.R, baseColor.G, baseColor.B));
                bg.Freeze();
                var border = new SolidColorBrush(Color.FromArgb(185, baseColor.R, baseColor.G, baseColor.B));
                border.Freeze();
                var text = new SolidColorBrush(Color.FromArgb(255,
                    (byte)Math.Min(255, baseColor.R + 70),
                    (byte)Math.Min(255, baseColor.G + 70),
                    (byte)Math.Min(255, baseColor.B + 70)));
                text.Freeze();

                ActiveGpuBadges.Add(new ActiveGpuBadge
                {
                    TechId = tech.Id,
                    Label = tech.BadgeText,
                    Vendor = tech.Vendor,
                    BackgroundBrush = bg,
                    BorderBrush = border,
                    TextBrush = text
                });
            }
        }

        OnPropertyChanged(nameof(HasActiveGpuBadges));
        SetMetricValue("gpu_tech_badges", ActiveGpuBadges.Count > 0
            ? string.Join(" · ", ActiveGpuBadges.Select(b => b.Label))
            : "--");
    }

    private Color GetBadgeColor(GpuVendor vendor)
    {
        if (UseCustomGpuBadgeColor && MetricItem.TryParseHex(CustomGpuBadgeColorHex, out var custom))
            return custom;

        return vendor switch
        {
            GpuVendor.Nvidia => Color.FromRgb(118, 185, 0),
            GpuVendor.Amd => Color.FromRgb(239, 68, 68),
            GpuVendor.Intel => Color.FromRgb(56, 189, 248),
            _ => Color.FromRgb(148, 163, 184)
        };
    }

    private void RebuildBackgroundImageBrush()
    {
        if (!HasBackgroundImage)
        {
            WidgetBackgroundImageBrush = null;
            OnPropertyChanged(nameof(WidgetBorderBrush));
            OnPropertyChanged(nameof(WidgetTextShadowOpacity));
            return;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(_backgroundImagePath, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();

            var brush = new ImageBrush(bmp)
            {
                Opacity = Math.Clamp(_backgroundImageOpacity / 100.0, 0.0, 1.0),
                Stretch = _backgroundImageStretch switch
                {
                    BackgroundImageStretchMode.Uniform => Stretch.Uniform,
                    BackgroundImageStretchMode.Fill => Stretch.Fill,
                    _ => Stretch.UniformToFill
                }
            };
            brush.Freeze();
            WidgetBackgroundImageBrush = brush;
        }
        catch { WidgetBackgroundImageBrush = null; }

        OnPropertyChanged(nameof(WidgetBorderBrush));
        OnPropertyChanged(nameof(WidgetTextShadowOpacity));
    }

    private void UpdateFpsMetricsVisibility()
    {
        foreach (var item in AllMetrics)
        {
            if (item.Category != MetricCategory.Fps)
            {
                item.IsVisibleOnWidget = true;
                continue;
            }

            if (item.IsGpuBadgesMetric)
            {
                item.IsVisibleOnWidget = HasActiveGpuBadges || !AutoHideFpsWhenIdle;
                continue;
            }

            if (AutoHideFpsWhenIdle && !IsGameActive)
            {
                item.IsVisibleOnWidget = false;
                continue;
            }

            if (item.Id == "fps_framegen" && AutoHideFrameGenWhenInactive && !_isFrameGenActive)
            {
                item.IsVisibleOnWidget = false;
                continue;
            }

            item.IsVisibleOnWidget = true;
        }
    }

    private void EnsureGameProfileExists(string exeName)
    {
        if (!_gameTechProfiles.ContainsKey(exeName))
            _gameTechProfiles[exeName] = new Dictionary<string, GameTechOverrideState>(StringComparer.OrdinalIgnoreCase);
        RebuildSavedGameProfileNames();
    }

    private void RebuildSavedGameProfileNames()
    {
        var current = CurrentOverrideGameExe;
        SavedGameProfileNames.Clear();
        foreach (var key in _gameTechProfiles.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            SavedGameProfileNames.Add(key);
        if (!string.IsNullOrWhiteSpace(current) && !SavedGameProfileNames.Contains(current, StringComparer.OrdinalIgnoreCase))
            SavedGameProfileNames.Insert(0, current);
    }

    private void SetMetricValue(string id, string value)
    {
        if (_metricsById.TryGetValue(id, out var item))
            item.FormattedValue = value;
    }

    private void PopulateMetricsFromService()
    {
        AvailableGroups.Clear();
        AllMetrics.Clear();
        _metricsById.Clear();

        var groups = new Dictionary<string, MetricGroupViewModel>();
        foreach (var metric in _hardwareService.DiscoveredMetrics)
        {
            metric.OnStyleChanged = ScheduleConfigSave;
            _metricsById[metric.Id] = metric;
            AllMetrics.Add(metric);

            if (!groups.TryGetValue(metric.GroupKey, out var groupVm))
            {
                groupVm = new MetricGroupViewModel
                {
                    GroupKey = metric.GroupKey,
                    Title = metric.GroupTitle,
                    Subtitle = metric.GroupSubtitle
                };
                groups[metric.GroupKey] = groupVm;
                AvailableGroups.Add(groupVm);
            }

            groupVm.Items.Add(metric);
        }
    }

    private void PopulateGpuTechnologies()
    {
        DetectedGpus.Clear();
        foreach (var gpu in _hardwareService.DetectedGpus)
            DetectedGpus.Add(gpu);

        GpuTechGroups.Clear();

        var nvidiaGroup = new GpuTechVendorGroupViewModel
        {
            Vendor = GpuVendor.Nvidia,
            Title = "NVIDIA",
            Subtitle = "Quét nvngx_dlss.dll, nvngx_dlssg.dll, nvngx_dlssd.dll, sl.reflex.dll và trạng thái driver",
            AccentBrush = Freeze("#76B900")
        };
        AddTech(nvidiaGroup, "nv_dlss", "DLSS", "DLSS", "Phát hiện nvngx_dlss.dll (dùng chung thư viện với DLAA — có thể chọn rõ ở mục Ghi đè nhanh)");
        AddTech(nvidiaGroup, "nv_dlaa", "DLAA", "DLAA", "Khử răng cưa AI độ phân giải gốc (bật qua Ghi đè nhanh cho game nếu dùng DLAA thay vì DLSS)");
        AddTech(nvidiaGroup, "nv_dlss_fg", "DLSS Frame Generation", "DLSS FG", "Phát hiện nvngx_dlssg.dll / sl.dlss_g.dll");
        AddTech(nvidiaGroup, "nv_smooth_motion", "Smooth Motion", "Smooth Motion", "Tạo khung hình cấp độ driver NVIDIA (kiểm tra cấu hình driver & PresentMon)");
        AddTech(nvidiaGroup, "nv_rt", "Ray Tracing", "RT", "Phát hiện thư viện DirectX Raytracing (DXR / RTX)");
        AddTech(nvidiaGroup, "nv_pt", "Path Tracing", "Path Tracing", "Phát hiện nvngx_dlssd.dll (Ray Reconstruction) hoặc bật thủ công theo hồ sơ game");
        AddTech(nvidiaGroup, "nv_reflex", "NVIDIA Reflex", "Reflex", "Phát hiện sl.reflex.dll / thư viện giảm độ trễ NVIDIA");
        GpuTechGroups.Add(nvidiaGroup);

        var amdGroup = new GpuTechVendorGroupViewModel
        {
            Vendor = GpuVendor.Amd,
            Title = "AMD",
            Subtitle = "Quét amd_fidelityfx*.dll, ffx_fsr*.dll và trạng thái driver AMD (AFMF / Anti-Lag)",
            AccentBrush = Freeze("#EF4444")
        };
        AddTech(amdGroup, "amd_fsr", "AMD FSR", "FSR", "Phát hiện amd_fidelityfx*.dll / ffx_fsr*.dll");
        AddTech(amdGroup, "amd_fg_afmf", "FSR Frame Generation & AFMF (Fluid Motion Frames)", "FSR FG / AFMF", "Phát hiện thư viện FSR Frame Generation hoặc trạng thái driver AFMF");
        AddTech(amdGroup, "amd_latency", "FSR Latency Reduction", "Anti-Lag", "Phát hiện thư viện / cấu hình driver AMD Anti-Lag");
        GpuTechGroups.Add(amdGroup);

        var intelGroup = new GpuTechVendorGroupViewModel
        {
            Vendor = GpuVendor.Intel,
            Title = "Intel",
            Subtitle = "Quét libxess.dll, libxess_fg.dll, libxell.dll và DirectX Raytracing",
            AccentBrush = Freeze("#38BDF8")
        };
        AddTech(intelGroup, "intel_xess_sr", "XeSS Super Resolution", "XeSS", "Phát hiện libxess.dll");
        AddTech(intelGroup, "intel_xess_fg", "XeSS Frame Generation / Multi-Frame Generation", "XeSS FG", "Phát hiện libxess_fg.dll");
        AddTech(intelGroup, "intel_xell", "Xe Low Latency (XeLL)", "XeLL", "Phát hiện libxell.dll");
        AddTech(intelGroup, "intel_rt", "Ray Tracing", "RT", "Phát hiện hỗ trợ DirectX Raytracing (DXR)");
        GpuTechGroups.Add(intelGroup);
    }

    private void AddTech(GpuTechVendorGroupViewModel group, string id, string name, string badgeText, string description)
    {
        group.Items.Add(new GpuTechItemViewModel
        {
            Id = id,
            Name = name,
            BadgeText = badgeText,
            Description = description,
            Vendor = group.Vendor,
            OnToggleChanged = () =>
            {
                RebuildActiveGpuBadges();
                UpdateFpsMetricsVisibility();
                ScheduleConfigSave();
            },
            OnOverrideChanged = OnTechOverrideChanged
        });
    }

    private void LoadFromConfig()
    {
        _isRestoring = true;
        try
        {
            var cfg = _configService.Load();

            var lang = SupportedLanguages.FirstOrDefault(l => l.Code.Equals(cfg.LanguageCode, StringComparison.OrdinalIgnoreCase))
                       ?? SupportedLanguages[0];
            _selectedLanguage = lang;
            LocalizationService.Instance.CurrentLanguageCode = lang.Code;

            _widgetLeft = cfg.WidgetLeft;
            _widgetTop = cfg.WidgetTop;
            _widgetWidth = cfg.WidgetWidth;
            _widgetHeight = cfg.WidgetHeight;
            _backgroundOpacity = cfg.BackgroundOpacity;
            _backgroundColorHex = cfg.BackgroundColorHex;
            _textColorHex = cfg.TextColorHex;

            // Font Family, FontSize (10-36), FontWeight (7 levels)
            _selectedFontFamily = InstalledFontFamilies.FirstOrDefault(f =>
                                      f.Source.Equals(cfg.FontFamilyName, StringComparison.OrdinalIgnoreCase))
                                  ?? InstalledFontFamilies.FirstOrDefault(f =>
                                      f.Source.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase))
                                  ?? InstalledFontFamilies.First();
            _fontSize = Math.Clamp(cfg.FontSize, 10, 36);
            _selectedFontWeightOption = FontWeightOptions.FirstOrDefault(w =>
                                            w.Key.Equals(cfg.FontWeightName, StringComparison.OrdinalIgnoreCase))
                                        ?? FontWeightOptions[4];

            _autoScaleContent = cfg.AutoScaleContent;
            _blurMode = cfg.BlurMode;
            _cornerRadius = cfg.CornerRadius;
            _isClickThrough = cfg.IsClickThrough;
            _isWidgetTopmost = cfg.IsWidgetTopmost;
            _isWidgetVisible = cfg.IsWidgetVisible;

            _backgroundImagePath = cfg.BackgroundImagePath ?? string.Empty;
            _backgroundImageOpacity = cfg.BackgroundImageOpacity;
            _backgroundImageStretch = cfg.BackgroundImageStretch;

            _autoHideFpsWhenIdle = cfg.AutoHideFpsWhenIdle;
            _autoHideFrameGenWhenInactive = cfg.AutoHideFrameGenWhenInactive;
            _fpsGraphHeight = cfg.FpsGraphHeight;
            _fpsGraphBaseColorHex = cfg.FpsGraphBaseColorHex;
            _fpsGraphFrameGenColorHex = cfg.FpsGraphFrameGenColorHex;

            _runAtWindowsStartup = Win32Helper.IsWindowsStartupEnabled() || cfg.RunAtWindowsStartup;
            _minimizeToTrayOnClose = cfg.MinimizeToTrayOnClose;
            _autoCheckUpdatesOnStartup = cfg.AutoCheckUpdatesOnStartup;

            foreach (var item in AllMetrics)
            {
                cfg.MetricStyles.TryGetValue(item.Id, out var style);
                item.ApplyStyleConfig(style);
            }

            ActiveMetrics.Clear();
            foreach (var item in _metricsById.Values)
                item.IsSelected = false;

            var idsToLoad = cfg.SelectedMetricIds is { Count: > 0 }
                ? cfg.SelectedMetricIds
                : _hardwareService.DefaultMetricIds.ToList();

            foreach (var id in idsToLoad)
            {
                if (_metricsById.TryGetValue(id, out var metric) && !metric.IsSelected)
                {
                    metric.IsSelected = true;
                    ActiveMetrics.Add(metric);
                }
            }

            if (ActiveMetrics.Count == 0)
            {
                foreach (var id in _hardwareService.DefaultMetricIds)
                {
                    if (_metricsById.TryGetValue(id, out var metric) && !metric.IsSelected)
                    {
                        metric.IsSelected = true;
                        ActiveMetrics.Add(metric);
                    }
                }
            }

            _gpuTechMode = cfg.GpuTechMode;
            _hideUnsupportedGpuTechs = cfg.HideUnsupportedGpuTechs;
            _useCustomGpuBadgeColor = cfg.UseCustomGpuBadgeColor;
            _customGpuBadgeColorHex = cfg.CustomGpuBadgeColorHex;
            _selectedPrimaryGpu = DetectedGpus.FirstOrDefault(g => g.Id == cfg.SelectedPrimaryGpuId)
                                  ?? DetectedGpus.FirstOrDefault();

            bool hasSavedTechs = cfg.EnabledGpuTechIds is { Count: > 0 };
            var enabledSet = new HashSet<string>(cfg.EnabledGpuTechIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (var group in GpuTechGroups)
                foreach (var tech in group.Items)
                    tech.SetEnabledSilent(!hasSavedTechs || enabledSet.Contains(tech.Id));

            _gameTechProfiles.Clear();
            foreach (var kv in cfg.GameTechProfiles)
            {
                if (!string.IsNullOrWhiteSpace(kv.Key) && kv.Value != null)
                    _gameTechProfiles[kv.Key] = new Dictionary<string, GameTechOverrideState>(kv.Value, StringComparer.OrdinalIgnoreCase);
            }
            RebuildSavedGameProfileNames();
            if (SavedGameProfileNames.Count > 0)
                _currentOverrideGameExe = SavedGameProfileNames[0];
        }
        finally
        {
            _isRestoring = false;
        }
    }

    private void RebuildFilteredCustomizationMetrics()
    {
        var previous = SelectedCustomizationMetric;
        FilteredCustomizationMetrics.Clear();

        IEnumerable<MetricItem> source = ShowOnlyActiveInCustomization ? ActiveMetrics : AllMetrics;
        foreach (var item in source)
            FilteredCustomizationMetrics.Add(item);

        if (previous != null && FilteredCustomizationMetrics.Contains(previous))
            SelectedCustomizationMetric = previous;
        else
            SelectedCustomizationMetric = FilteredCustomizationMetrics.FirstOrDefault();
    }

    private void RefreshAllMetricsInheritedTheme()
    {
        var labelBrush = WidgetSecondaryTextBrush;
        var valueBrush = WidgetTextBrush;
        bool isLight = IsLightBackground;

        foreach (var metric in AllMetrics)
            metric.RefreshInheritedTheme(labelBrush, valueBrush, isLight, ContentScaleFactor);
    }

    private void UpdateGpuVendorSupportStates()
    {
        var activeVendors = new HashSet<GpuVendor>();

        if (GpuTechMode == GpuTechMode.SinglePrimaryGpu && SelectedPrimaryGpu != null)
            activeVendors.Add(SelectedPrimaryGpu.Vendor);
        else
            foreach (var gpu in DetectedGpus)
                activeVendors.Add(gpu.Vendor);

        SupportedGpuTechItems.Clear();

        foreach (var group in GpuTechGroups)
        {
            bool hasOnMachine = DetectedGpus.Any(g => g.Vendor == group.Vendor);
            bool supported = activeVendors.Contains(group.Vendor);

            group.IsSupported = supported;
            group.IsVisible = supported || !HideUnsupportedGpuTechs;

            if (supported)
            {
                var names = DetectedGpus
                    .Where(g => g.Vendor == group.Vendor &&
                                (GpuTechMode == GpuTechMode.AutoAllGpus || g.Id == SelectedPrimaryGpu?.Id))
                    .Select(g => g.Name);
                group.SupportBadgeText = $"Khả dụng • {string.Join(", ", names)}";

                foreach (var item in group.Items)
                    SupportedGpuTechItems.Add(item);
            }
            else if (hasOnMachine && GpuTechMode == GpuTechMode.SinglePrimaryGpu)
            {
                group.SupportBadgeText = "Không thuộc GPU chính đã chọn";
            }
            else
            {
                group.SupportBadgeText = "Không có GPU hãng này trên máy";
            }
        }

        RebuildActiveGpuBadges();
    }

    public void ApplySensorSnapshot(IReadOnlyDictionary<string, string> snapshot)
    {
        foreach (var kv in snapshot)
        {
            if (_metricsById.TryGetValue(kv.Key, out var item))
                item.FormattedValue = kv.Value;
        }
    }

    public void ToggleMetric(MetricItem item)
    {
        if (item.IsSelected) RemoveMetric(item);
        else AddMetricAt(item.Id, ActiveMetrics.Count);
    }

    public void AddMetricAt(string metricId, int insertIndex)
    {
        if (!_metricsById.TryGetValue(metricId, out var item)) return;

        if (item.IsSelected)
        {
            int existingIdx = ActiveMetrics.IndexOf(item);
            if (existingIdx >= 0)
            {
                int target = Math.Clamp(insertIndex, 0, ActiveMetrics.Count - 1);
                if (existingIdx != target) MoveActiveMetric(existingIdx, target);
            }
            return;
        }

        int clampedIdx = Math.Clamp(insertIndex, 0, ActiveMetrics.Count);
        item.IsSelected = true;
        ActiveMetrics.Insert(clampedIdx, item);
        ScheduleConfigSave();
    }

    public void RemoveMetric(MetricItem item)
    {
        if (!item.IsSelected) return;
        item.IsSelected = false;
        ActiveMetrics.Remove(item);
        ScheduleConfigSave();
    }

    public void MoveActiveMetric(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= ActiveMetrics.Count) return;
        int targetIndex = Math.Clamp(newIndex, 0, ActiveMetrics.Count - 1);
        if (oldIndex == targetIndex) return;
        ActiveMetrics.Move(oldIndex, targetIndex);
        ScheduleConfigSave();
    }

    public void UpdateWidgetBounds(double left, double top, double width, double height)
    {
        bool changed = false;
        if (Math.Abs(_widgetLeft - left) > 0.5)
        {
            _widgetLeft = Math.Round(left);
            OnPropertyChanged(nameof(WidgetLeft));
            changed = true;
        }
        if (Math.Abs(_widgetTop - top) > 0.5)
        {
            _widgetTop = Math.Round(top);
            OnPropertyChanged(nameof(WidgetTop));
            changed = true;
        }
        double clampedW = Math.Clamp(Math.Round(width), 150, 2400);
        if (Math.Abs(_widgetWidth - clampedW) > 0.5)
        {
            _widgetWidth = clampedW;
            OnPropertyChanged(nameof(WidgetWidth));
            changed = true;
        }
        double clampedH = Math.Clamp(Math.Round(height), 42, 1600);
        if (Math.Abs(_widgetHeight - clampedH) > 0.5)
        {
            _widgetHeight = clampedH;
            OnPropertyChanged(nameof(WidgetHeight));
            changed = true;
        }

        RecalculateContentScale(_widgetWidth, _widgetHeight);

        if (changed) ScheduleConfigSave();
    }

    public void ResetToDefaults()
    {
        _isRestoring = true;
        try
        {
            BackgroundOpacity = 78;
            BackgroundColorHex = "#141419";
            TextColorHex = "#FFFFFF";
            SelectedFontFamily = InstalledFontFamilies.FirstOrDefault(f => f.Source.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase))
                                 ?? InstalledFontFamilies.First();
            FontSize = 13;
            SelectedFontWeightOption = FontWeightOptions[4];
            AutoScaleContent = true;
            BlurMode = WidgetBlurMode.Off;
            CornerRadius = 14;
            IsClickThrough = false;
            IsWidgetTopmost = true;
            IsWidgetVisible = true;
            WidgetWidth = ScaleBaselineWidth;
            WidgetHeight = ScaleBaselineHeight;

            BackgroundImagePath = string.Empty;
            BackgroundImageOpacity = 85;
            BackgroundImageStretch = BackgroundImageStretchMode.UniformToFill;

            AutoHideFpsWhenIdle = false;
            AutoHideFrameGenWhenInactive = false;
            FpsGraphHeight = 46;
            FpsGraphBaseColorHex = "#4ADE80";
            FpsGraphFrameGenColorHex = "#60CDFF";
            MinimizeToTrayOnClose = true;
            AutoCheckUpdatesOnStartup = true;

            UseCustomGpuBadgeColor = false;
            CustomGpuBadgeColorHex = "#60CDFF";

            foreach (var item in AllMetrics)
            {
                item.IsSelected = false;
                item.ApplyStyleConfig(null);
            }
            ActiveMetrics.Clear();

            foreach (var id in _hardwareService.DefaultMetricIds)
            {
                if (_metricsById.TryGetValue(id, out var metric))
                {
                    metric.IsSelected = true;
                    ActiveMetrics.Add(metric);
                }
            }

            GpuTechMode = GpuTechMode.AutoAllGpus;
            HideUnsupportedGpuTechs = false;
            SelectedPrimaryGpu = DetectedGpus.FirstOrDefault();
            foreach (var group in GpuTechGroups)
                foreach (var tech in group.Items)
                    tech.SetEnabledSilent(true);
        }
        finally
        {
            _isRestoring = false;
        }

        RecalculateContentScale(_widgetWidth, _widgetHeight);
        RefreshAllMetricsInheritedTheme();
        UpdateGpuVendorSupportStates();
        RebuildActiveGpuBadges();
        UpdateFpsMetricsVisibility();
        BlurSettingsChanged?.Invoke();
        CenterWidgetRequested?.Invoke();
        SaveConfigNow();
    }

    public AppConfig BuildConfigSnapshot()
    {
        var styles = new Dictionary<string, MetricStyleConfig>();
        foreach (var item in AllMetrics)
        {
            var exported = item.ExportStyleConfig();
            if (exported != null)
                styles[item.Id] = exported;
        }

        var enabledGpuTechs = GpuTechGroups
            .SelectMany(g => g.Items)
            .Where(t => t.IsEnabled)
            .Select(t => t.Id)
            .ToList();

        var profilesCopy = new Dictionary<string, Dictionary<string, GameTechOverrideState>>();
        foreach (var kv in _gameTechProfiles)
        {
            if (kv.Value.Count > 0)
                profilesCopy[kv.Key] = new Dictionary<string, GameTechOverrideState>(kv.Value);
        }

        return new AppConfig
        {
            LanguageCode = _selectedLanguage.Code,
            WidgetLeft = _widgetLeft,
            WidgetTop = _widgetTop,
            WidgetWidth = _widgetWidth,
            WidgetHeight = _widgetHeight,
            SelectedMetricIds = ActiveMetrics.Select(m => m.Id).ToList(),
            MetricStyles = styles,
            BackgroundOpacity = _backgroundOpacity,
            BackgroundColorHex = _backgroundColorHex,
            TextColorHex = _textColorHex,
            FontFamilyName = _selectedFontFamily.Source,
            FontSize = _fontSize,
            FontWeightName = _selectedFontWeightOption.Key,
            AutoScaleContent = _autoScaleContent,
            BlurMode = _blurMode,
            CornerRadius = _cornerRadius,
            IsClickThrough = _isClickThrough,
            IsWidgetTopmost = _isWidgetTopmost,
            IsWidgetVisible = _isWidgetVisible,
            BackgroundImagePath = string.IsNullOrWhiteSpace(_backgroundImagePath) ? null : _backgroundImagePath,
            BackgroundImageOpacity = _backgroundImageOpacity,
            BackgroundImageStretch = _backgroundImageStretch,
            AutoHideFpsWhenIdle = _autoHideFpsWhenIdle,
            AutoHideFrameGenWhenInactive = _autoHideFrameGenWhenInactive,
            FpsGraphHeight = _fpsGraphHeight,
            FpsGraphBaseColorHex = _fpsGraphBaseColorHex,
            FpsGraphFrameGenColorHex = _fpsGraphFrameGenColorHex,
            RunAtWindowsStartup = _runAtWindowsStartup,
            MinimizeToTrayOnClose = _minimizeToTrayOnClose,
            AutoCheckUpdatesOnStartup = _autoCheckUpdatesOnStartup,
            GpuTechMode = _gpuTechMode,
            SelectedPrimaryGpuId = _selectedPrimaryGpu?.Id,
            HideUnsupportedGpuTechs = _hideUnsupportedGpuTechs,
            EnabledGpuTechIds = enabledGpuTechs,
            UseCustomGpuBadgeColor = _useCustomGpuBadgeColor,
            CustomGpuBadgeColorHex = _customGpuBadgeColorHex,
            GameTechProfiles = profilesCopy
        };
    }

    public void SaveConfigNow() => _configService.SaveImmediate(BuildConfigSnapshot());

    private void ScheduleConfigSave()
    {
        if (_isRestoring) return;
        _configService.ScheduleSave(BuildConfigSnapshot);
    }

    private static string NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string s = value.Trim();
        if (!s.StartsWith('#')) s = "#" + s;
        return s.ToUpperInvariant();
    }

    private static ColorPreset MakePreset(string name, string hex)
    {
        MetricItem.TryParseHex(hex, out var color);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return new ColorPreset { Name = name, Hex = hex, Brush = brush };
    }

    private static SolidColorBrush CreateFrozenBrush(string hex, Color fallback)
    {
        var c = MetricItem.TryParseHex(hex, out var parsed) ? parsed : fallback;
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private static SolidColorBrush Freeze(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
