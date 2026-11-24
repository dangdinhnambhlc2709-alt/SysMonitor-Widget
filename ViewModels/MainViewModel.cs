using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WinHardwareMonitor.Models;
using WinHardwareMonitor.Services;
using Wpf.Ui.Controls;

namespace WinHardwareMonitor.ViewModels;

public sealed class MetricGroupViewModel : ObservableObject
{
    public string GroupKey { get; init; } = string.Empty;
    public Func<string> TitleProvider { get; init; } = () => string.Empty;
    public Func<string> SubtitleProvider { get; init; } = () => string.Empty;
    public string Title => TitleProvider();
    public string Subtitle => SubtitleProvider();
    public ObservableCollection<MetricItem> Items { get; } = new();

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
    }
}

public sealed class ColorPreset : ObservableObject
{
    public string NameKey { get; init; } = string.Empty;
    public string Name => LocalizationService.Instance[NameKey];
    public string Hex { get; init; } = "#FFFFFF";
    public SolidColorBrush Brush { get; init; } = new(Colors.White);

    public void RefreshLocalization() => OnPropertyChanged(nameof(Name));
}

public sealed class IconChoice : ObservableObject
{
    public string NameKey { get; init; } = string.Empty;
    public string Name => LocalizationService.Instance[NameKey];
    public string SymbolName { get; init; } = string.Empty;
    public SymbolRegular Symbol { get; init; }

    public void RefreshLocalization() => OnPropertyChanged(nameof(Name));
}

public sealed class FontWeightOption : ObservableObject
{
    public string Key { get; }
    public string DisplayKey { get; }
    public string DisplayName => LocalizationService.Instance[DisplayKey];
    public FontWeight Weight { get; }
    public FontWeight LabelWeight { get; }

    public FontWeightOption(string key, string displayKey, FontWeight weight, FontWeight labelWeight)
    {
        Key = key;
        DisplayKey = displayKey;
        Weight = weight;
        LabelWeight = labelWeight;
    }

    public void RefreshLocalization() => OnPropertyChanged(nameof(DisplayName));
}

public sealed class MainViewModel : ObservableObject
{
    private static readonly SolidColorBrush TransparentBrush = Freeze(new SolidColorBrush(Colors.Transparent));

    private readonly ConfigService _configService;
    private readonly HardwareMonitorService _hardwareService;
    private readonly PresentMonService _presentMonService;
    private readonly GpuTechDetectorService _gpuTechDetectorService;
    private readonly UpdateService _updateService;
    private readonly Dictionary<string, MetricItem> _metricsById = new();
    private readonly Dictionary<string, Dictionary<string, GameTechOverrideState>> _gameTechProfiles = new(StringComparer.OrdinalIgnoreCase);

    private bool _isRestoring;

    // Ngôn ngữ & Tab
    private LanguageOption _selectedLanguage;
    private int _selectedTabIndex;
    private int _rightInspectorTabIndex; // 0 = Lớp đang chọn, 1 = Kiểu chung & Khung

    // Vị trí Overlay OSD thuần túy (9 điểm neo + X/Y + đa màn hình + Hotkey)
    private OverlayAnchor _overlayAnchor = OverlayAnchor.TopLeft;
    private double _overlayOffsetX = 16;
    private double _overlayOffsetY = 16;
    private MonitorOption? _selectedMonitor;
    private bool _isWidgetTopmost = true;
    private bool _isWidgetVisible = true;
    private bool _enableToggleOverlayHotkey = true;
    private bool _isHotkeyRegistered;

    // Canvas thiết kế kiểu RTSS Overlay Editor
    private bool _showEditorGrid = true;
    private bool _snapToGrid = true;
    private int _gridSnapSize = 8;
    private double _cellPaddingH = 8;
    private double _cellPaddingV = 4;
    private double _globalLabelValueGap = 8;
    private CellContentAlignment _globalContentAlignment = CellContentAlignment.InlineWithGap;
    private OverlayLayerViewModel? _selectedLayer;

    // Phông chữ & Kiểu dáng ô phẳng mặc định chung
    private FontFamily _selectedFontFamily = new("Segoe UI");
    private double _fontSize = 13;
    private FontWeightOption _selectedFontWeightOption;
    private string _textColorHex = "#FFFFFF";
    private string _labelColorHex = "#60CDFF";
    private SolidColorBrush _globalValueBrush = Freeze(new SolidColorBrush(Colors.White));
    private SolidColorBrush _globalLabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(96, 205, 255)));

    private string _defaultCellBgColorHex = "#0A0C10";
    private double _defaultCellBgOpacity = 78;
    private bool _defaultCellBorderEnabled = true;
    private string _defaultCellBorderColorHex = "#FFFFFF";
    private double _defaultCellBorderOpacity = 22;
    private double _defaultCellBorderThickness = 1.0;
    private double _defaultCellCornerRadius;

    // Nền khung tổng bọc ngoài bảng Overlay
    private bool _showOuterFrame;
    private string _outerFrameBgColorHex = "#080A0E";
    private double _outerFrameBgOpacity = 80;
    private bool _outerFrameBorderEnabled = true;
    private string _outerFrameBorderColorHex = "#FFFFFF";
    private double _outerFrameBorderOpacity = 25;
    private double _outerFrameBorderThickness = 1.0;
    private double _outerFrameCornerRadius = 6;
    private double _outerFramePadding = 6;

    private SolidColorBrush _cachedOuterBgBrush = TransparentBrush;
    private SolidColorBrush _cachedOuterBorderBrush = TransparentBrush;

    // Công nghệ GPU 3 lớp & Hybrid Laptop
    private bool _hideUnsupportedGpuTechs;
    private bool _useCustomGpuBadgeColor;
    private string _customGpuBadgeColorHex = "#60CDFF";
    private string _gpuDetectionStatusText = string.Empty;
    private GpuTechDetectionSnapshot? _lastGpuTechSnapshot;
    private bool _isGpuTechGameActive;
    private string _currentOverrideGameExe = string.Empty;
    private string _manualGameExeInput = string.Empty;
    private Dictionary<string, GpuTechDetectionSource> _lastAutoDetectedTechSources = new(StringComparer.OrdinalIgnoreCase);

    // FPS & PresentMon
    private bool _autoHideFpsWhenIdle;
    private bool _autoHideFrameGenWhenInactive;
    private double _fpsGraphHeight = 46;
    private string _fpsGraphBaseColorHex = "#4ADE80";
    private string _fpsGraphFrameGenColorHex = "#60CDFF";
    private string _fpsStatusText = string.Empty;
    private FpsSnapshot? _lastFpsSnapshot;
    private bool _isPresentMonAvailable;
    private bool _isPresentMonRunning;
    private bool _isGameActive;
    private bool _isFrameGenActive;
    private bool _isDownloadingPresentMon;
    private string _presentMonDownloadFeedback = string.Empty;
    private IReadOnlyList<FpsGraphPoint> _fpsGraphHistory = Array.Empty<FpsGraphPoint>();

    // Hệ thống & Cập nhật
    private bool _runAtWindowsStartup;
    private bool _minimizeToTrayOnClose = true;
    private bool _autoCheckUpdatesOnStartup = true;
    private bool _isCheckingUpdates;
    private string _updateStatusMessage = string.Empty;
    private string _startupStatusFeedback = string.Empty;

    public ObservableCollection<MetricGroupViewModel> AvailableGroups { get; } = new();
    public ObservableCollection<MetricItem> AllMetrics { get; } = new();
    public ObservableCollection<OverlayLayerViewModel> OverlayLayers { get; } = new();
    public ObservableCollection<MonitorOption> Monitors { get; } = new();

    public ObservableCollection<DetectedGpuInfo> DetectedGpus { get; } = new();
    public ObservableCollection<GpuTechVendorGroupViewModel> GpuTechGroups { get; } = new();
    public ObservableCollection<GpuTechItemViewModel> SupportedGpuTechItems { get; } = new();
    public ObservableCollection<ActiveGpuBadge> ActiveGpuBadges { get; } = new();
    public ObservableCollection<string> SavedGameProfileNames { get; } = new();

    public IReadOnlyList<ColorPreset> ColorPresets { get; }
    public IReadOnlyList<ColorPreset> DarkBgPresets { get; }
    public IReadOnlyList<IconChoice> FluentIconChoices { get; }
    public IReadOnlyList<LanguageOption> SupportedLanguages => LocalizationService.Instance.SupportedLanguages;
    public IReadOnlyList<FontFamily> InstalledFontFamilies { get; }
    public IReadOnlyList<FontWeightOption> FontWeightOptions { get; }
    public IReadOnlyList<AlignmentOption> AlignmentOptions { get; }
    public IReadOnlyList<DisplayModeOption> DisplayModeOptions { get; }
    public IReadOnlyList<int> GridSnapOptions { get; } = new[] { 4, 8, 12, 16 };

    public bool IsAdministrator { get; }
    public string AdminStatusText => IsAdministrator
        ? LocalizationService.Instance["AdminRunning"]
        : LocalizationService.Instance["AdminMissing"];

    public string ConfigFilePath => _configService.ConfigPath;
    public string CurrentVersionDisplay => $"v{_updateService.CurrentVersionString}";
    public string GitHubRepoDisplay => $"{UpdateService.GitHubOwner}/{UpdateService.GitHubRepo}";
    public string PresentMonPathDisplay => string.IsNullOrWhiteSpace(_presentMonService.PresentMonPath)
        ? LocalizationService.Instance["PresentMonMissingPath"]
        : _presentMonService.PresentMonPath;

    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value != null && SetProperty(ref _selectedLanguage, value))
            {
                LocalizationService.Instance.CurrentLanguageCode = value.Code;
                ScheduleConfigSave();
            }
        }
    }

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public int RightInspectorTabIndex
    {
        get => _rightInspectorTabIndex;
        set => SetProperty(ref _rightInspectorTabIndex, value);
    }

    // --- Vị trí Overlay OSD & Hotkey ---
    public bool IsHotkeyRegistered
    {
        get => _isHotkeyRegistered;
        set
        {
            if (SetProperty(ref _isHotkeyRegistered, value))
                OnPropertyChanged(nameof(HotkeyStatusText));
        }
    }

    public bool EnableToggleOverlayHotkey
    {
        get => _enableToggleOverlayHotkey;
        set
        {
            if (SetProperty(ref _enableToggleOverlayHotkey, value))
            {
                ToggleOverlayHotkeySettingChanged?.Invoke(value);
                OnPropertyChanged(nameof(HotkeyStatusText));
                ScheduleConfigSave();
            }
        }
    }

    public string HotkeyStatusText
    {
        get
        {
            var loc = LocalizationService.Instance;
            if (!EnableToggleOverlayHotkey)
                return loc["HotkeyStatusDisabled"];
            return IsHotkeyRegistered ? loc["HotkeyStatusOk"] : loc["HotkeyStatusBusy"];
        }
    }

    public OverlayAnchor OverlayAnchor
    {
        get => _overlayAnchor;
        set
        {
            if (SetProperty(ref _overlayAnchor, value))
            {
                NotifyAnchorProperties();
                OverlayPositionChanged?.Invoke();
                ScheduleConfigSave();
            }
        }
    }

    public bool IsAnchorTopLeft => _overlayAnchor == OverlayAnchor.TopLeft;
    public bool IsAnchorTopCenter => _overlayAnchor == OverlayAnchor.TopCenter;
    public bool IsAnchorTopRight => _overlayAnchor == OverlayAnchor.TopRight;
    public bool IsAnchorMiddleLeft => _overlayAnchor == OverlayAnchor.MiddleLeft;
    public bool IsAnchorMiddleCenter => _overlayAnchor == OverlayAnchor.MiddleCenter;
    public bool IsAnchorMiddleRight => _overlayAnchor == OverlayAnchor.MiddleRight;
    public bool IsAnchorBottomLeft => _overlayAnchor == OverlayAnchor.BottomLeft;
    public bool IsAnchorBottomCenter => _overlayAnchor == OverlayAnchor.BottomCenter;
    public bool IsAnchorBottomRight => _overlayAnchor == OverlayAnchor.BottomRight;

    public double OverlayOffsetX
    {
        get => _overlayOffsetX;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 800);
            if (SetProperty(ref _overlayOffsetX, clamped))
            {
                OnPropertyChanged(nameof(OverlayOffsetXLabel));
                OverlayPositionChanged?.Invoke();
                ScheduleConfigSave();
            }
        }
    }

    public string OverlayOffsetXLabel => $"{OverlayOffsetX:F0} px";

    public double OverlayOffsetY
    {
        get => _overlayOffsetY;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 800);
            if (SetProperty(ref _overlayOffsetY, clamped))
            {
                OnPropertyChanged(nameof(OverlayOffsetYLabel));
                OverlayPositionChanged?.Invoke();
                ScheduleConfigSave();
            }
        }
    }

    public string OverlayOffsetYLabel => $"{OverlayOffsetY:F0} px";

    public MonitorOption? SelectedMonitor
    {
        get => _selectedMonitor;
        set
        {
            if (SetProperty(ref _selectedMonitor, value))
            {
                OverlayPositionChanged?.Invoke();
                ScheduleConfigSave();
            }
        }
    }

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

    // --- Thiết lập Canvas kiểu RTSS Overlay Editor ---
    public bool ShowEditorGrid
    {
        get => _showEditorGrid;
        set
        {
            if (SetProperty(ref _showEditorGrid, value))
            {
                EditorCanvasGridChanged?.Invoke();
                ScheduleConfigSave();
            }
        }
    }

    public bool SnapToGrid
    {
        get => _snapToGrid;
        set
        {
            if (SetProperty(ref _snapToGrid, value))
                ScheduleConfigSave();
        }
    }

    public int GridSnapSize
    {
        get => _gridSnapSize;
        set
        {
            int norm = value is 4 or 8 or 12 or 16 ? value : 8;
            if (SetProperty(ref _gridSnapSize, norm))
            {
                EditorCanvasGridChanged?.Invoke();
                ScheduleConfigSave();
            }
        }
    }

    public string LayerCountText => LocalizationService.Instance.Format("LayerCountFormat", OverlayLayers.Count);

    public OverlayLayerViewModel? SelectedLayer
    {
        get => _selectedLayer;
        set
        {
            if (ReferenceEquals(_selectedLayer, value)) return;
            if (_selectedLayer != null) _selectedLayer.IsSelected = false;
            _selectedLayer = value;
            if (_selectedLayer != null) _selectedLayer.IsSelected = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedLayer));
            OnPropertyChanged(nameof(HasNoSelectedLayer));
            OnPropertyChanged(nameof(SelectedLayerStatusBarText));
        }
    }

    public bool HasSelectedLayer => _selectedLayer != null;
    public bool HasNoSelectedLayer => _selectedLayer == null;

    public string SelectedLayerStatusBarText => _selectedLayer != null
        ? $"{_selectedLayer.LayerTitle}  •  {_selectedLayer.GeometrySummary}"
        : LocalizationService.Instance["CanvasStatusIdle"];

    public double CellPaddingH
    {
        get => _cellPaddingH;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 24);
            if (SetProperty(ref _cellPaddingH, clamped))
            {
                OnPropertyChanged(nameof(CellPaddingHLabel));
                OnPropertyChanged(nameof(EffectiveCellPadding));
                RefreshAllLayerVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public string CellPaddingHLabel => $"{CellPaddingH:F0} px";

    public double CellPaddingV
    {
        get => _cellPaddingV;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 20);
            if (SetProperty(ref _cellPaddingV, clamped))
            {
                OnPropertyChanged(nameof(CellPaddingVLabel));
                OnPropertyChanged(nameof(EffectiveCellPadding));
                RefreshAllLayerVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public string CellPaddingVLabel => $"{CellPaddingV:F0} px";
    public Thickness EffectiveCellPadding => new(_cellPaddingH, _cellPaddingV, _cellPaddingH, _cellPaddingV);

    public double GlobalLabelValueGap
    {
        get => _globalLabelValueGap;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 60);
            if (SetProperty(ref _globalLabelValueGap, clamped))
            {
                OnPropertyChanged(nameof(GlobalLabelValueGapLabel));
                RefreshAllLayerVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public string GlobalLabelValueGapLabel => $"{GlobalLabelValueGap:F0} px";

    public CellContentAlignment GlobalContentAlignment
    {
        get => _globalContentAlignment;
        set
        {
            if (SetProperty(ref _globalContentAlignment, value))
            {
                RefreshAllLayerVisuals();
                ScheduleConfigSave();
            }
        }
    }

    // --- Phông chữ & Màu sắc mặc định chung ---
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

    public double FontSize
    {
        get => _fontSize;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 10, 36);
            if (SetProperty(ref _fontSize, clamped))
            {
                OnPropertyChanged(nameof(FontSizeLabel));
                OnPropertyChanged(nameof(WidgetBadgeFontSize));
                RefreshAllLayerVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public string FontSizeLabel => $"{FontSize:F0} px";
    public double WidgetBadgeFontSize => Math.Max(9.5, Math.Round(FontSize * 0.80, 1));

    public string TextColorHex
    {
        get => _textColorHex;
        set
        {
            string norm = NormalizeHex(value);
            if (SetProperty(ref _textColorHex, norm) && MetricItem.TryParseHex(norm, out var c))
            {
                _globalValueBrush = Freeze(new SolidColorBrush(c));
                OnPropertyChanged(nameof(GlobalValueBrush));
                RefreshAllLayerVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public string LabelColorHex
    {
        get => _labelColorHex;
        set
        {
            string norm = NormalizeHex(value);
            if (SetProperty(ref _labelColorHex, norm) && MetricItem.TryParseHex(norm, out var c))
            {
                _globalLabelBrush = Freeze(new SolidColorBrush(c));
                OnPropertyChanged(nameof(GlobalLabelBrush));
                RefreshAllLayerVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public SolidColorBrush GlobalValueBrush => _globalValueBrush;
    public SolidColorBrush GlobalLabelBrush => _globalLabelBrush;

    // --- Kiểu dáng ô phẳng mặc định chung ---
    public string DefaultCellBgColorHex
    {
        get => _defaultCellBgColorHex;
        set
        {
            string norm = NormalizeHex(value);
            if (SetProperty(ref _defaultCellBgColorHex, norm) && MetricItem.TryParseHex(norm, out _))
            {
                OnPropertyChanged(nameof(DefaultCellBgPreviewBrush));
                RefreshAllLayerVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public SolidColorBrush DefaultCellBgPreviewBrush => CreateFrozenBrush(_defaultCellBgColorHex, Color.FromRgb(10, 12, 16));

    public double DefaultCellBgOpacity
    {
        get => _defaultCellBgOpacity;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 100);
            if (SetProperty(ref _defaultCellBgOpacity, clamped))
            {
                OnPropertyChanged(nameof(DefaultCellBgOpacityLabel));
                RefreshAllLayerVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public string DefaultCellBgOpacityLabel => $"{DefaultCellBgOpacity:F0}%";

    public bool DefaultCellBorderEnabled
    {
        get => _defaultCellBorderEnabled;
        set
        {
            if (SetProperty(ref _defaultCellBorderEnabled, value))
            {
                RefreshAllLayerVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public string DefaultCellBorderColorHex
    {
        get => _defaultCellBorderColorHex;
        set
        {
            string norm = NormalizeHex(value);
            if (SetProperty(ref _defaultCellBorderColorHex, norm) && MetricItem.TryParseHex(norm, out _))
            {
                OnPropertyChanged(nameof(DefaultCellBorderPreviewBrush));
                RefreshAllLayerVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public SolidColorBrush DefaultCellBorderPreviewBrush => CreateFrozenBrush(_defaultCellBorderColorHex, Colors.White);

    public double DefaultCellBorderOpacity
    {
        get => _defaultCellBorderOpacity;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 100);
            if (SetProperty(ref _defaultCellBorderOpacity, clamped))
            {
                OnPropertyChanged(nameof(DefaultCellBorderOpacityLabel));
                RefreshAllLayerVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public string DefaultCellBorderOpacityLabel => $"{DefaultCellBorderOpacity:F0}%";

    public double DefaultCellBorderThickness
    {
        get => _defaultCellBorderThickness;
        set
        {
            double clamped = Math.Clamp(Math.Round(value * 2.0) / 2.0, 0.5, 4.0);
            if (SetProperty(ref _defaultCellBorderThickness, clamped))
            {
                OnPropertyChanged(nameof(DefaultCellBorderThicknessLabel));
                RefreshAllLayerVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public string DefaultCellBorderThicknessLabel => $"{DefaultCellBorderThickness:F1} px";

    public double DefaultCellCornerRadius
    {
        get => _defaultCellCornerRadius;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 20);
            if (SetProperty(ref _defaultCellCornerRadius, clamped))
            {
                OnPropertyChanged(nameof(DefaultCellCornerRadiusLabel));
                RefreshAllLayerVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public string DefaultCellCornerRadiusLabel => $"{DefaultCellCornerRadius:F0} px";

    // --- Nền khung tổng bọc ngoài bảng Overlay ---
    public bool ShowOuterFrame
    {
        get => _showOuterFrame;
        set
        {
            if (SetProperty(ref _showOuterFrame, value))
            {
                RebuildOuterFrameVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public string OuterFrameBgColorHex
    {
        get => _outerFrameBgColorHex;
        set
        {
            string norm = NormalizeHex(value);
            if (SetProperty(ref _outerFrameBgColorHex, norm) && MetricItem.TryParseHex(norm, out _))
            {
                OnPropertyChanged(nameof(OuterFrameBgPreviewBrush));
                RebuildOuterFrameVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public SolidColorBrush OuterFrameBgPreviewBrush => CreateFrozenBrush(_outerFrameBgColorHex, Color.FromRgb(8, 10, 14));

    public double OuterFrameBgOpacity
    {
        get => _outerFrameBgOpacity;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 100);
            if (SetProperty(ref _outerFrameBgOpacity, clamped))
            {
                OnPropertyChanged(nameof(OuterFrameBgOpacityLabel));
                RebuildOuterFrameVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public string OuterFrameBgOpacityLabel => $"{OuterFrameBgOpacity:F0}%";

    public bool OuterFrameBorderEnabled
    {
        get => _outerFrameBorderEnabled;
        set
        {
            if (SetProperty(ref _outerFrameBorderEnabled, value))
            {
                RebuildOuterFrameVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public string OuterFrameBorderColorHex
    {
        get => _outerFrameBorderColorHex;
        set
        {
            string norm = NormalizeHex(value);
            if (SetProperty(ref _outerFrameBorderColorHex, norm) && MetricItem.TryParseHex(norm, out _))
            {
                OnPropertyChanged(nameof(OuterFrameBorderPreviewBrush));
                RebuildOuterFrameVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public SolidColorBrush OuterFrameBorderPreviewBrush => CreateFrozenBrush(_outerFrameBorderColorHex, Colors.White);

    public double OuterFrameBorderOpacity
    {
        get => _outerFrameBorderOpacity;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 100);
            if (SetProperty(ref _outerFrameBorderOpacity, clamped))
            {
                OnPropertyChanged(nameof(OuterFrameBorderOpacityLabel));
                RebuildOuterFrameVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public string OuterFrameBorderOpacityLabel => $"{OuterFrameBorderOpacity:F0}%";

    public double OuterFrameBorderThickness
    {
        get => _outerFrameBorderThickness;
        set
        {
            double clamped = Math.Clamp(Math.Round(value * 2.0) / 2.0, 0.5, 4.0);
            if (SetProperty(ref _outerFrameBorderThickness, clamped))
            {
                OnPropertyChanged(nameof(OuterFrameBorderThicknessLabel));
                RebuildOuterFrameVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public string OuterFrameBorderThicknessLabel => $"{OuterFrameBorderThickness:F1} px";

    public double OuterFrameCornerRadius
    {
        get => _outerFrameCornerRadius;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 28);
            if (SetProperty(ref _outerFrameCornerRadius, clamped))
            {
                OnPropertyChanged(nameof(OuterFrameCornerRadiusLabel));
                RebuildOuterFrameVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public string OuterFrameCornerRadiusLabel => $"{OuterFrameCornerRadius:F0} px";

    public double OuterFramePadding
    {
        get => _outerFramePadding;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 24);
            if (SetProperty(ref _outerFramePadding, clamped))
            {
                OnPropertyChanged(nameof(OuterFramePaddingLabel));
                RebuildOuterFrameVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public string OuterFramePaddingLabel => $"{OuterFramePadding:F0} px";

    public SolidColorBrush EffectiveOuterFrameBackground => _cachedOuterBgBrush;
    public SolidColorBrush EffectiveOuterFrameBorderBrush => _cachedOuterBorderBrush;
    public Thickness EffectiveOuterFrameBorderThickness => (_showOuterFrame && _outerFrameBorderEnabled) ? new Thickness(_outerFrameBorderThickness) : new Thickness(0);
    public CornerRadius EffectiveOuterFrameCornerRadius => _showOuterFrame ? new CornerRadius(_outerFrameCornerRadius) : new CornerRadius(0);
    public Thickness EffectiveOuterFramePadding => _showOuterFrame ? new Thickness(_outerFramePadding) : new Thickness(0);

    // --- Công nghệ GPU & Huy hiệu ---
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
            if (SetProperty(ref _customGpuBadgeColorHex, norm) && MetricItem.TryParseHex(norm, out _))
            {
                OnPropertyChanged(nameof(CustomGpuBadgePreviewBrush));
                RebuildActiveGpuBadges();
                ScheduleConfigSave();
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
        ? LocalizationService.Instance.Format("OverrideProfileFormat", _currentOverrideGameExe)
        : LocalizationService.Instance["OverrideNoGame"];

    // --- FPS & PresentMon ---
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
                RefreshAllLayerVisuals();
                ScheduleConfigSave();
            }
        }
    }

    public string FpsGraphHeightLabel => $"{FpsGraphHeight:F0} px";

    public string FpsGraphBaseColorHex
    {
        get => _fpsGraphBaseColorHex;
        set
        {
            string norm = NormalizeHex(value);
            if (SetProperty(ref _fpsGraphBaseColorHex, norm) && MetricItem.TryParseHex(norm, out _))
            {
                OnPropertyChanged(nameof(FpsGraphBaseBrush));
                ScheduleConfigSave();
            }
        }
    }

    public string FpsGraphFrameGenColorHex
    {
        get => _fpsGraphFrameGenColorHex;
        set
        {
            string norm = NormalizeHex(value);
            if (SetProperty(ref _fpsGraphFrameGenColorHex, norm) && MetricItem.TryParseHex(norm, out _))
            {
                OnPropertyChanged(nameof(FpsGraphFrameGenBrush));
                ScheduleConfigSave();
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

    // --- Hệ thống & Cập nhật ---
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
                    ? LocalizationService.Instance["StartupEnabledMsg"]
                    : LocalizationService.Instance["StartupDisabledMsg"];
                ScheduleConfigSave();
            }
            else
            {
                StartupStatusFeedback = LocalizationService.Instance.Format("StartupErrorMsg", err ?? string.Empty);
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
        ? LocalizationService.Instance["FooterMinToTray"]
        : LocalizationService.Instance["FooterExitOnClose"];

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

    // --- Events ---
    public event Action<bool>? WidgetTopmostChanged;
    public event Action<bool>? WidgetVisibilityChanged;
    public event Action<bool>? ToggleOverlayHotkeySettingChanged;
    public event Action? OverlayPositionChanged;
    public event Action? EditorCanvasGridChanged;
    public event Action? OpenSettingsRequested;
    public event Action? ExitAppRequested;
    public event Action<UpdateReleaseInfo>? UpdateAvailableDetected;

    // --- Commands ---
    public ICommand QuickAddMetricCommand { get; }
    public ICommand AddStaticTextLayerCommand { get; }
    public ICommand AddBackdropBoxLayerCommand { get; }
    public ICommand RemoveLayerCommand { get; }
    public ICommand RemoveSelectedLayerCommand { get; }
    public ICommand DuplicateSelectedLayerCommand { get; }
    public ICommand BringLayerForwardCommand { get; }
    public ICommand SendLayerBackwardCommand { get; }
    public ICommand AutoSizeSelectedLayerCommand { get; }
    public ICommand ClearAllLayersCommand { get; }
    public ICommand ApplyPresetPillCommand { get; }
    public ICommand ApplyPresetClassicCommand { get; }
    public ICommand ApplyPresetCardsCommand { get; }
    public ICommand ApplySelectedLayerStyleToAllCommand { get; }
    public ICommand ResetSelectedLayerLabelCommand { get; }
    public ICommand ClearSelectedLayerLabelColorCommand { get; }
    public ICommand ClearSelectedLayerValueColorCommand { get; }
    public ICommand SelectLayerLabelColorCommand { get; }
    public ICommand SelectLayerValueColorCommand { get; }
    public ICommand SelectLayerBgColorCommand { get; }
    public ICommand SelectLayerBorderColorCommand { get; }
    public ICommand SelectLayerIconCommand { get; }
    public ICommand ResetSelectedLayerStyleCommand { get; }
    public ICommand SelectGlobalValueColorCommand { get; }
    public ICommand SelectGlobalLabelColorCommand { get; }
    public ICommand SelectDefaultCellBgColorCommand { get; }
    public ICommand SelectDefaultCellBorderColorCommand { get; }
    public ICommand SelectOuterFrameBgColorCommand { get; }
    public ICommand SelectOuterFrameBorderColorCommand { get; }
    public ICommand SetAnchorCommand { get; }
    public ICommand RefreshMonitorsCommand { get; }
    public ICommand ToggleWidgetVisibilityCommand { get; }
    public ICommand ResetDefaultsCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ExitAppCommand { get; }
    public ICommand ResetFpsStatsCommand { get; }
    public ICommand DownloadPresentMonCommand { get; }
    public ICommand CycleTechOverrideCommand { get; }
    public ICommand ApplyManualGameExeCommand { get; }
    public ICommand ClearCurrentGameOverridesCommand { get; }
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

        InstalledFontFamilies = Fonts.SystemFontFamilies
            .OrderBy(f => f.Source, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        FontWeightOptions = new List<FontWeightOption>
        {
            new("Thin", "WeightThin", FontWeights.Thin, FontWeights.Thin),
            new("Light", "WeightLight", FontWeights.Light, FontWeights.Light),
            new("Normal", "WeightNormal", FontWeights.Normal, FontWeights.Light),
            new("Medium", "WeightMedium", FontWeights.Medium, FontWeights.Normal),
            new("SemiBold", "WeightSemiBold", FontWeights.SemiBold, FontWeights.Medium),
            new("Bold", "WeightBold", FontWeights.Bold, FontWeights.Medium),
            new("ExtraBold", "WeightExtraBold", FontWeights.ExtraBold, FontWeights.SemiBold)
        };
        _selectedFontWeightOption = FontWeightOptions[4];

        AlignmentOptions = new List<AlignmentOption>
        {
            new(CellContentAlignment.InlineWithGap, "AlignInlineGap"),
            new(CellContentAlignment.SpaceBetween, "AlignSpaceBetween"),
            new(CellContentAlignment.StackedVertical, "AlignStacked")
        };

        DisplayModeOptions = new List<DisplayModeOption>
        {
            new(LayerDisplayMode.LabelAndValue, "DisplayModeLabelValue"),
            new(LayerDisplayMode.ValueOnly, "DisplayModeValueOnly"),
            new(LayerDisplayMode.LabelOnly, "DisplayModeLabelOnly")
        };

        ColorPresets = new List<ColorPreset>
        {
            MakePreset("ColorWhite", "#FFFFFF"),
            MakePreset("ColorSilver", "#CBD5E1"),
            MakePreset("ColorCharcoal", "#141418"),
            MakePreset("ColorWin11Blue", "#60CDFF"),
            MakePreset("ColorTeal", "#2DD4BF"),
            MakePreset("ColorGreen", "#4ADE80"),
            MakePreset("ColorNvidiaGreen", "#76B900"),
            MakePreset("ColorYellow", "#FACC15"),
            MakePreset("ColorOrange", "#FB923C"),
            MakePreset("ColorCoralRed", "#F87171"),
            MakePreset("ColorPurple", "#C084FC")
        };

        DarkBgPresets = new List<ColorPreset>
        {
            MakePreset("ColorCharcoal", "#0A0C10"),
            MakePreset("ColorSilver", "#1E2430"),
            MakePreset("ColorWin11Blue", "#0B1E2D"),
            MakePreset("ColorGreen", "#0D1F17"),
            MakePreset("ColorPurple", "#1B1329"),
            MakePreset("ColorWhite", "#F8FAFC")
        };

        FluentIconChoices = new List<IconChoice>
        {
            new() { NameKey = "IconCpu", SymbolName = nameof(SymbolRegular.DeveloperBoard24), Symbol = SymbolRegular.DeveloperBoard24 },
            new() { NameKey = "IconTemp", SymbolName = nameof(SymbolRegular.Temperature24), Symbol = SymbolRegular.Temperature24 },
            new() { NameKey = "IconClockFg", SymbolName = nameof(SymbolRegular.TopSpeed24), Symbol = SymbolRegular.TopSpeed24 },
            new() { NameKey = "IconPower", SymbolName = nameof(SymbolRegular.Flash24), Symbol = SymbolRegular.Flash24 },
            new() { NameKey = "IconGpuFps", SymbolName = nameof(SymbolRegular.Games24), Symbol = SymbolRegular.Games24 },
            new() { NameKey = "IconFpsGraph", SymbolName = nameof(SymbolRegular.ChartMultiple24), Symbol = SymbolRegular.ChartMultiple24 },
            new() { NameKey = "IconVram", SymbolName = nameof(SymbolRegular.DesktopPulse24), Symbol = SymbolRegular.DesktopPulse24 },
            new() { NameKey = "IconGaugeAvg", SymbolName = nameof(SymbolRegular.Gauge24), Symbol = SymbolRegular.Gauge24 },
            new() { NameKey = "IconRam", SymbolName = nameof(SymbolRegular.Ram20), Symbol = SymbolRegular.Ram20 },
            new() { NameKey = "IconMemory", SymbolName = nameof(SymbolRegular.Memory16), Symbol = SymbolRegular.Memory16 },
            new() { NameKey = "IconDiskRead", SymbolName = nameof(SymbolRegular.HardDrive24), Symbol = SymbolRegular.HardDrive24 },
            new() { NameKey = "IconDiskWrite", SymbolName = nameof(SymbolRegular.Storage24), Symbol = SymbolRegular.Storage24 },
            new() { NameKey = "IconNetDown", SymbolName = nameof(SymbolRegular.ArrowDownload28), Symbol = SymbolRegular.ArrowDownload28 },
            new() { NameKey = "IconNetUp", SymbolName = nameof(SymbolRegular.ArrowUpload24), Symbol = SymbolRegular.ArrowUpload24 },
            new() { NameKey = "IconNetwork", SymbolName = nameof(SymbolRegular.Globe28), Symbol = SymbolRegular.Globe28 },
            new() { NameKey = "IconLow1", SymbolName = nameof(SymbolRegular.DataUsage24), Symbol = SymbolRegular.DataUsage24 },
            new() { NameKey = "IconLow01", SymbolName = nameof(SymbolRegular.ClockAlarm24), Symbol = SymbolRegular.ClockAlarm24 },
            new() { NameKey = "IconSystem", SymbolName = nameof(SymbolRegular.Server24), Symbol = SymbolRegular.Server24 }
        };

        // Commands
        QuickAddMetricCommand = new RelayCommand(p => { if (p is MetricItem item) QuickToggleMetricOnCanvas(item); });
        AddStaticTextLayerCommand = new RelayCommand(AddStaticTextLayer);
        AddBackdropBoxLayerCommand = new RelayCommand(AddBackdropBoxLayer);
        RemoveLayerCommand = new RelayCommand(p => { if (p is OverlayLayerViewModel layer) RemoveLayer(layer); });
        RemoveSelectedLayerCommand = new RelayCommand(() => { if (SelectedLayer != null) RemoveLayer(SelectedLayer); });
        DuplicateSelectedLayerCommand = new RelayCommand(DuplicateSelectedLayer);
        BringLayerForwardCommand = new RelayCommand(() => { if (SelectedLayer != null) SelectedLayer.ZIndex++; });
        SendLayerBackwardCommand = new RelayCommand(() => { if (SelectedLayer != null) SelectedLayer.ZIndex = Math.Max(0, SelectedLayer.ZIndex - 1); });
        AutoSizeSelectedLayerCommand = new RelayCommand(() =>
        {
            if (SelectedLayer == null || SelectedLayer.IsBackdropBoxLayer) return;
            SelectedLayer.Width = 0;
            SelectedLayer.Height = 0;
        });
        ClearAllLayersCommand = new RelayCommand(() =>
        {
            OverlayLayers.Clear();
            foreach (var m in AllMetrics) m.IsPlacedOnGrid = false;
            SelectedLayer = null;
            ScheduleConfigSave();
        });

        ApplyPresetPillCommand = new RelayCommand(ApplyPresetPill);
        ApplyPresetClassicCommand = new RelayCommand(ApplyPresetClassic);
        ApplyPresetCardsCommand = new RelayCommand(ApplyPresetCards);

        ApplySelectedLayerStyleToAllCommand = new RelayCommand(() =>
        {
            if (SelectedLayer == null) return;
            foreach (var layer in OverlayLayers)
            {
                if (!ReferenceEquals(layer, SelectedLayer))
                    layer.CopyVisualStyleFrom(SelectedLayer);
            }
            ScheduleConfigSave();
        });

        ResetSelectedLayerLabelCommand = new RelayCommand(() => { if (SelectedLayer != null) SelectedLayer.CustomLabel = string.Empty; });
        ClearSelectedLayerLabelColorCommand = new RelayCommand(() => { if (SelectedLayer != null) SelectedLayer.LabelColorHex = string.Empty; });
        ClearSelectedLayerValueColorCommand = new RelayCommand(() => { if (SelectedLayer != null) SelectedLayer.ValueColorHex = string.Empty; });
        SelectLayerLabelColorCommand = new RelayCommand(p => { if (SelectedLayer != null && p is string h) SelectedLayer.LabelColorHex = h; });
        SelectLayerValueColorCommand = new RelayCommand(p => { if (SelectedLayer != null && p is string h) SelectedLayer.ValueColorHex = h; });
        SelectLayerBgColorCommand = new RelayCommand(p =>
        {
            if (SelectedLayer != null && p is string h)
            {
                SelectedLayer.UseGlobalStyle = false;
                SelectedLayer.BgColorHex = h;
            }
        });
        SelectLayerBorderColorCommand = new RelayCommand(p =>
        {
            if (SelectedLayer != null && p is string h)
            {
                SelectedLayer.UseGlobalStyle = false;
                SelectedLayer.BorderColorHex = h;
            }
        });
        SelectLayerIconCommand = new RelayCommand(p =>
        {
            if (SelectedLayer != null && p is string sym)
            {
                SelectedLayer.CustomSymbolName = sym;
                SelectedLayer.HeaderStyle = MetricHeaderStyle.Icon;
            }
        });
        ResetSelectedLayerStyleCommand = new RelayCommand(() =>
        {
            if (SelectedLayer == null) return;
            SelectedLayer.ApplyStyleConfig(new CellStyleConfig
            {
                UseGlobalStyle = !SelectedLayer.IsBackdropBoxLayer,
                DisplayMode = SelectedLayer.IsStaticTextLayer ? LayerDisplayMode.LabelOnly : LayerDisplayMode.LabelAndValue,
                HeaderStyle = MetricHeaderStyle.Text,
                BackgroundColorHex = DefaultCellBgColorHex,
                BackgroundOpacity = DefaultCellBgOpacity,
                BorderEnabled = DefaultCellBorderEnabled,
                BorderColorHex = DefaultCellBorderColorHex,
                BorderOpacity = DefaultCellBorderOpacity,
                BorderThickness = DefaultCellBorderThickness,
                CornerRadius = DefaultCellCornerRadius
            });
            ScheduleConfigSave();
        });

        SelectGlobalValueColorCommand = new RelayCommand(p => { if (p is string h) TextColorHex = h; });
        SelectGlobalLabelColorCommand = new RelayCommand(p => { if (p is string h) LabelColorHex = h; });
        SelectDefaultCellBgColorCommand = new RelayCommand(p => { if (p is string h) DefaultCellBgColorHex = h; });
        SelectDefaultCellBorderColorCommand = new RelayCommand(p => { if (p is string h) DefaultCellBorderColorHex = h; });
        SelectOuterFrameBgColorCommand = new RelayCommand(p => { if (p is string h) OuterFrameBgColorHex = h; });
        SelectOuterFrameBorderColorCommand = new RelayCommand(p => { if (p is string h) OuterFrameBorderColorHex = h; });

        SetAnchorCommand = new RelayCommand(p =>
        {
            if (p is OverlayAnchor anchor) OverlayAnchor = anchor;
            else if (p is string s && Enum.TryParse<OverlayAnchor>(s, true, out var parsed)) OverlayAnchor = parsed;
        });

        RefreshMonitorsCommand = new RelayCommand(() => RefreshMonitorsList(SelectedMonitor?.DeviceName));
        ToggleWidgetVisibilityCommand = new RelayCommand(() => IsWidgetVisible = !IsWidgetVisible);
        ResetDefaultsCommand = new RelayCommand(ResetToDefaults);
        OpenSettingsCommand = new RelayCommand(() => OpenSettingsRequested?.Invoke());
        ExitAppCommand = new RelayCommand(() => ExitAppRequested?.Invoke());

        ResetFpsStatsCommand = new RelayCommand(ResetFpsStats);
        DownloadPresentMonCommand = new RelayCommand(async () =>
        {
            if (IsDownloadingPresentMon) return;
            IsDownloadingPresentMon = true;
            PresentMonDownloadFeedback = LocalizationService.Instance["PmDownloading"];
            try
            {
                var (_, msg) = await _presentMonService.DownloadOfficialPresentMonAsync();
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
            if (!raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) raw += ".exe";
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
        CheckForUpdatesCommand = new RelayCommand(async () => await CheckForUpdatesManualAsync());

        OverlayLayers.CollectionChanged += (_, _) => OnPropertyChanged(nameof(LayerCountText));

        PopulateMetricsFromService();
        PopulateGpuTechnologies();
        _gpuTechDetectorService.SetKnownGpus(DetectedGpus);
        LoadFromConfig();

        _fpsStatusText = LocalizationService.Instance["PmInitializing"];
        _gpuDetectionStatusText = LocalizationService.Instance["GpuDetWaiting"];
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        RebuildOuterFrameVisuals();
        RefreshAllLayerVisuals();
        UpdateGpuVendorSupportStates();
        SyncGameOverridesToViewModel();
        RebuildActiveGpuBadges();
        UpdateFpsMetricsVisibility();
        OnLanguageChanged();
    }

    // --- RTSS-Style Canvas Layer Operations ---
    public double SnapCoordinate(double value, bool bypassSnap = false)
    {
        double clamped = Math.Max(0, value);
        if (bypassSnap || !SnapToGrid || _gridSnapSize <= 1)
            return Math.Round(clamped);
        return Math.Round(clamped / _gridSnapSize) * _gridSnapSize;
    }

    public void SelectLayer(OverlayLayerViewModel? layer)
    {
        SelectedLayer = layer;
        if (layer != null)
            RightInspectorTabIndex = 0;
    }

    public void NotifyLayerEdited()
    {
        OnPropertyChanged(nameof(SelectedLayerStatusBarText));
        ScheduleConfigSave();
    }

    public void AddMetricLayerAt(string metricId, double x, double y)
    {
        if (!_metricsById.TryGetValue(metricId, out var metric))
            return;

        double sx = SnapCoordinate(x);
        double sy = SnapCoordinate(y);
        int nextZ = OverlayLayers.Count > 0 ? OverlayLayers.Max(l => l.ZIndex) + 1 : 10;

        double initW = metric.IsGraphMetric ? 200 : 0;
        double initH = metric.IsGraphMetric ? Math.Max(56, FpsGraphHeight + 20) : 0;

        var layer = new OverlayLayerViewModel(this, OverlayLayerKind.Metric, metric, sx, sy, initW, initH, nextZ);
        OverlayLayers.Add(layer);
        SyncMetricPlacedFlags();
        SelectLayer(layer);
        ScheduleConfigSave();
    }

    public void QuickToggleMetricOnCanvas(MetricItem item)
    {
        var existing = OverlayLayers.FirstOrDefault(l => l.IsMetricLayer && l.MetricId == item.Id);
        if (existing != null)
        {
            RemoveLayer(existing);
            return;
        }

        // Tìm vị trí gọn gàng ngay bên dưới lớp thấp nhất hiện tại
        double nextY = OverlayLayers.Count > 0
            ? SnapCoordinate(OverlayLayers.Max(l => l.Y + (l.Height > 0 ? l.Height : 28)) + 8)
            : 16;
        if (nextY > 420) nextY = 16;

        AddMetricLayerAt(item.Id, 16, nextY);
    }

    private void AddStaticTextLayer()
    {
        int nextZ = OverlayLayers.Count > 0 ? OverlayLayers.Max(l => l.ZIndex) + 1 : 10;
        double x = SelectedLayer != null ? SnapCoordinate(SelectedLayer.X + 16) : 16;
        double y = SelectedLayer != null ? SnapCoordinate(SelectedLayer.Y + 32) : 16;
        var layer = new OverlayLayerViewModel(this, OverlayLayerKind.StaticText, null, x, y, 56, 32, nextZ);
        OverlayLayers.Add(layer);
        SelectLayer(layer);
        ScheduleConfigSave();
    }

    private void AddBackdropBoxLayer()
    {
        int minZ = OverlayLayers.Count > 0 ? Math.Max(0, OverlayLayers.Min(l => l.ZIndex) - 1) : 1;
        var layer = new OverlayLayerViewModel(this, OverlayLayerKind.BackdropBox, null, 16, 16, 360, 40, minZ);
        OverlayLayers.Insert(0, layer);
        SelectLayer(layer);
        ScheduleConfigSave();
    }

    public void DuplicateSelectedLayer()
    {
        if (SelectedLayer == null) return;
        int nextZ = OverlayLayers.Count > 0 ? OverlayLayers.Max(l => l.ZIndex) + 1 : 10;
        var dup = new OverlayLayerViewModel(
            this,
            SelectedLayer.Kind,
            SelectedLayer.Metric,
            SnapCoordinate(SelectedLayer.X + 16),
            SnapCoordinate(SelectedLayer.Y + 16),
            SelectedLayer.Width,
            SelectedLayer.Height,
            nextZ);
        dup.ApplyStyleConfig(SelectedLayer.ExportStyleConfig());
        OverlayLayers.Add(dup);
        SyncMetricPlacedFlags();
        SelectLayer(dup);
        ScheduleConfigSave();
    }

    public void RemoveLayer(OverlayLayerViewModel layer)
    {
        if (OverlayLayers.Remove(layer))
        {
            SyncMetricPlacedFlags();
            if (ReferenceEquals(SelectedLayer, layer))
                SelectedLayer = OverlayLayers.FirstOrDefault();
            ScheduleConfigSave();
        }
    }

    public void NudgeSelectedLayer(double dx, double dy)
    {
        if (SelectedLayer == null) return;
        SelectedLayer.X = Math.Max(0, SelectedLayer.X + dx);
        SelectedLayer.Y = Math.Max(0, SelectedLayer.Y + dy);
    }

    // --- 3 Bố cục mẫu kiểu RTSS Overlay Editor (1 chạm áp dụng ngay!) ---

    /// <summary>
    /// Mẫu 1: Thanh ngang Pill kiểu RTSS (pill.ovl): | CPU | 50°C | 45W | GPU | 41°C | 32W | FPS | 62 |
    /// </summary>
    public void ApplyPresetPill()
    {
        _isRestoring = true;
        try
        {
            ShowOuterFrame = false;
            DefaultCellBgColorHex = "#0A0C10";
            DefaultCellBgOpacity = 84;
            DefaultCellBorderEnabled = true;
            DefaultCellBorderColorHex = "#FFFFFF";
            DefaultCellBorderOpacity = 26;
            DefaultCellBorderThickness = 1.0;
            DefaultCellCornerRadius = 0;
            CellPaddingH = 8;
            CellPaddingV = 4;
            GlobalLabelValueGap = 6;
            GlobalContentAlignment = CellContentAlignment.InlineWithGap;
            TextColorHex = "#FFFFFF";
            LabelColorHex = "#60CDFF";

            OverlayLayers.Clear();
            double x = 16;
            double y = 16;
            double h = 32;
            int z = 10;

            void AddLabelPill(string text, string colorHex, double w)
            {
                var l = new OverlayLayerViewModel(this, OverlayLayerKind.StaticText, null, x, y, w, h, z++);
                l.ApplyStyleConfig(new CellStyleConfig
                {
                    UseGlobalStyle = true,
                    CustomLabel = text,
                    DisplayMode = LayerDisplayMode.LabelOnly,
                    LabelColorHex = colorHex
                });
                OverlayLayers.Add(l);
                x += w;
            }

            void AddValPill(string metricId, double w, string? valColor = null)
            {
                if (!_metricsById.TryGetValue(metricId, out var m)) return;
                var l = new OverlayLayerViewModel(this, OverlayLayerKind.Metric, m, x, y, w, h, z++);
                l.ApplyStyleConfig(new CellStyleConfig
                {
                    UseGlobalStyle = true,
                    DisplayMode = LayerDisplayMode.ValueOnly,
                    ValueColorHex = valColor
                });
                OverlayLayers.Add(l);
                x += w;
            }

            AddLabelPill("CPU", "#60CDFF", 48);
            AddValPill("cpu_temp", 56);
            AddValPill("cpu_load", 48);
            AddValPill("cpu_power", 56);

            AddLabelPill("GPU", "#2DD4BF", 48);
            AddValPill("gpu_0_temp", 56);
            AddValPill("gpu_0_load", 48);
            AddValPill("gpu_0_power", 56);

            AddLabelPill("RAM", "#C084FC", 48);
            AddValPill("ram_percent", 56);

            AddLabelPill("FPS", "#4ADE80", 48);
            AddValPill("fps_base", 64, "#4ADE80");

            // Thêm hàng Huy hiệu Công nghệ GPU ngay bên dưới thanh Pill
            if (_metricsById.TryGetValue("gpu_tech_badges", out var badges))
            {
                var bLayer = new OverlayLayerViewModel(this, OverlayLayerKind.Metric, badges, 16, y + h + 4, 0, 0, z++);
                bLayer.ApplyStyleConfig(new CellStyleConfig
                {
                    UseGlobalStyle = false,
                    BackgroundOpacity = 0,
                    BorderEnabled = false
                });
                OverlayLayers.Add(bLayer);
            }

            SyncMetricPlacedFlags();
            SelectedLayer = OverlayLayers.FirstOrDefault();
        }
        finally
        {
            _isRestoring = false;
        }

        RebuildOuterFrameVisuals();
        RefreshAllLayerVisuals();
        SaveConfigNow();
    }

    /// <summary>
    /// Mẫu 2: Cột gọn kinh điển RTSS (chữ sắc nét không khung, nhóm GPU / CPU / RAM / FPS + đường đồ thị FPS bên dưới).
    /// </summary>
    public void ApplyPresetClassic()
    {
        _isRestoring = true;
        try
        {
            ShowOuterFrame = false;
            DefaultCellBgColorHex = "#0A0C10";
            DefaultCellBgOpacity = 0;
            DefaultCellBorderEnabled = false;
            DefaultCellCornerRadius = 0;
            CellPaddingH = 4;
            CellPaddingV = 1;
            GlobalLabelValueGap = 8;
            GlobalContentAlignment = CellContentAlignment.InlineWithGap;
            TextColorHex = "#FACC15";
            LabelColorHex = "#60CDFF";

            OverlayLayers.Clear();
            int z = 10;

            void AddRowLabel(string text, string colorHex, double y)
            {
                var l = new OverlayLayerViewModel(this, OverlayLayerKind.StaticText, null, 16, y, 48, 22, z++);
                l.ApplyStyleConfig(new CellStyleConfig
                {
                    UseGlobalStyle = true,
                    CustomLabel = text,
                    DisplayMode = LayerDisplayMode.LabelOnly,
                    LabelColorHex = colorHex
                });
                OverlayLayers.Add(l);
            }

            void AddRowVal(string metricId, double x, double y, string colorHex)
            {
                if (!_metricsById.TryGetValue(metricId, out var m)) return;
                var l = new OverlayLayerViewModel(this, OverlayLayerKind.Metric, m, x, y, 0, 22, z++);
                l.ApplyStyleConfig(new CellStyleConfig
                {
                    UseGlobalStyle = true,
                    DisplayMode = LayerDisplayMode.ValueOnly,
                    ValueColorHex = colorHex
                });
                OverlayLayers.Add(l);
            }

            // Hàng 1: GPU
            AddRowLabel("GPU", "#4ADE80", 16);
            AddRowVal("gpu_0_temp", 72, 16, "#FB923C");
            AddRowVal("gpu_0_load", 128, 16, "#FB923C");
            AddRowVal("gpu_0_power", 176, 16, "#FB923C");

            // Hàng 2: VRAM
            AddRowLabel("VRAM", "#4ADE80", 40);
            AddRowVal("gpu_0_vram", 72, 40, "#FACC15");

            // Hàng 3: CPU
            AddRowLabel("CPU", "#60CDFF", 64);
            AddRowVal("cpu_temp", 72, 64, "#FB923C");
            AddRowVal("cpu_load", 128, 64, "#FB923C");
            AddRowVal("cpu_power", 176, 64, "#FB923C");

            // Hàng 4: RAM
            AddRowLabel("RAM", "#C084FC", 88);
            AddRowVal("ram_usage", 72, 88, "#FACC15");

            // Hàng 5: FPS
            AddRowLabel("FPS", "#F87171", 112);
            AddRowVal("fps_base", 72, 112, "#FFFFFF");
            AddRowVal("fps_1low", 144, 112, "#CBD5E1");

            // Hàng 6: Đồ thị FPS mỏng gọn bên dưới
            if (_metricsById.TryGetValue("fps_graph", out var graph))
            {
                var gLayer = new OverlayLayerViewModel(this, OverlayLayerKind.Metric, graph, 16, 140, 224, 56, z++);
                gLayer.ApplyStyleConfig(new CellStyleConfig
                {
                    UseGlobalStyle = true,
                    DisplayMode = LayerDisplayMode.ValueOnly
                });
                OverlayLayers.Add(gLayer);
            }

            SyncMetricPlacedFlags();
            SelectedLayer = OverlayLayers.FirstOrDefault();
        }
        finally
        {
            _isRestoring = false;
        }

        RebuildOuterFrameVisuals();
        RefreshAllLayerVisuals();
        SaveConfigNow();
    }

    /// <summary>
    /// Mẫu 3: Thẻ phẳng hiện đại bo góc gọn gàng xếp 2 hàng.
    /// </summary>
    public void ApplyPresetCards()
    {
        _isRestoring = true;
        try
        {
            ShowOuterFrame = false;
            DefaultCellBgColorHex = "#0E1117";
            DefaultCellBgOpacity = 80;
            DefaultCellBorderEnabled = true;
            DefaultCellBorderColorHex = "#FFFFFF";
            DefaultCellBorderOpacity = 18;
            DefaultCellBorderThickness = 1.0;
            DefaultCellCornerRadius = 6;
            CellPaddingH = 10;
            CellPaddingV = 5;
            GlobalLabelValueGap = 10;
            GlobalContentAlignment = CellContentAlignment.SpaceBetween;
            TextColorHex = "#FFFFFF";
            LabelColorHex = "#94A3B8";

            OverlayLayers.Clear();
            string[] ids = { "cpu_load", "cpu_temp", "gpu_0_load", "gpu_0_temp", "ram_usage", "fps_base", "net_down", "disk_total_read" };
            int z = 10;
            int idx = 0;
            foreach (var id in ids)
            {
                if (!_metricsById.TryGetValue(id, out var m)) continue;
                int col = idx % 4;
                int row = idx / 4;
                var l = new OverlayLayerViewModel(this, OverlayLayerKind.Metric, m, 16 + col * 136, 16 + row * 40, 128, 32, z++);
                OverlayLayers.Add(l);
                idx++;
            }

            if (_metricsById.TryGetValue("fps_graph", out var graph))
            {
                var g = new OverlayLayerViewModel(this, OverlayLayerKind.Metric, graph, 16, 100, 264, 64, z++);
                OverlayLayers.Add(g);
            }
            if (_metricsById.TryGetValue("gpu_tech_badges", out var badges))
            {
                var b = new OverlayLayerViewModel(this, OverlayLayerKind.Metric, badges, 288, 100, 264, 0, z++);
                OverlayLayers.Add(b);
            }

            SyncMetricPlacedFlags();
            SelectedLayer = OverlayLayers.FirstOrDefault();
        }
        finally
        {
            _isRestoring = false;
        }

        RebuildOuterFrameVisuals();
        RefreshAllLayerVisuals();
        SaveConfigNow();
    }

    private void SyncMetricPlacedFlags()
    {
        var placed = new HashSet<string>(OverlayLayers.Where(l => l.IsMetricLayer).Select(l => l.MetricId), StringComparer.OrdinalIgnoreCase);
        foreach (var m in AllMetrics)
            m.IsPlacedOnGrid = placed.Contains(m.Id);
    }

    // --- Live Language Refresh ---
    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(AdminStatusText));
        OnPropertyChanged(nameof(PresentMonPathDisplay));
        OnPropertyChanged(nameof(HotkeyStatusText));
        OnPropertyChanged(nameof(OverrideTargetGameDisplay));
        OnPropertyChanged(nameof(CloseBehaviorFooterText));
        OnPropertyChanged(nameof(WidgetVisibilityButtonText));
        OnPropertyChanged(nameof(LayerCountText));
        OnPropertyChanged(nameof(SelectedLayerStatusBarText));

        FpsStatusText = _lastFpsSnapshot != null
            ? PresentMonService.FormatStatusText(
                _lastFpsSnapshot.StatusKind,
                _lastFpsSnapshot.StatusErrorDetail,
                _lastFpsSnapshot.ActiveGameName,
                _lastFpsSnapshot.FrameGenSource)
            : LocalizationService.Instance["PmInitializing"];

        GpuDetectionStatusText = _lastGpuTechSnapshot != null
            ? GpuTechDetectionSnapshot.FormatNote(
                _lastGpuTechSnapshot.StatusKind,
                _lastGpuTechSnapshot.ForegroundExeName,
                _lastGpuTechSnapshot.LoadedModuleCount,
                _lastGpuTechSnapshot.ActiveRenderingGpuName)
            : LocalizationService.Instance["GpuDetWaiting"];

        foreach (var g in AvailableGroups) g.RefreshLocalization();
        foreach (var m in AllMetrics) m.RefreshLocalization();
        foreach (var l in OverlayLayers) l.RefreshLocalization();
        foreach (var mon in Monitors) mon.RefreshLocalization();
        foreach (var gpu in DetectedGpus) gpu.RefreshLocalization();
        foreach (var group in GpuTechGroups) group.RefreshLocalization();
        foreach (var opt in FontWeightOptions) opt.RefreshLocalization();
        foreach (var opt in AlignmentOptions) opt.RefreshLocalization();
        foreach (var opt in DisplayModeOptions) opt.RefreshLocalization();
        foreach (var preset in ColorPresets) preset.RefreshLocalization();
        foreach (var preset in DarkBgPresets) preset.RefreshLocalization();
        foreach (var icon in FluentIconChoices) icon.RefreshLocalization();

        UpdateGpuVendorSupportStates();
    }

    // --- Updates ---
    public async System.Threading.Tasks.Task CheckForUpdatesManualAsync()
    {
        if (IsCheckingUpdates) return;
        IsCheckingUpdates = true;
        UpdateStatusMessage = LocalizationService.Instance["UpdateChecking"];
        try
        {
            var result = await _updateService.CheckForUpdatesAsync();
            UpdateStatusMessage = result.StatusMessage;
            if (result.HasUpdate && result.Release != null)
                UpdateAvailableDetected?.Invoke(result.Release);
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
        catch { }
    }

    // --- Real-time Sensor, FPS & 3-Layer GPU Tech Feeds ---
    public void ResetFpsStats()
    {
        _presentMonService.ResetStats();
        FpsGraphHistory = Array.Empty<FpsGraphPoint>();
    }

    public void ApplySensorSnapshot(IReadOnlyDictionary<string, string> snapshot)
    {
        foreach (var kv in snapshot)
        {
            if (_metricsById.TryGetValue(kv.Key, out var item))
                item.FormattedValue = kv.Value;
        }
    }

    public void ApplyFpsSnapshot(FpsSnapshot snap)
    {
        _lastFpsSnapshot = snap;
        IsPresentMonAvailable = snap.IsPresentMonAvailable;
        IsPresentMonRunning = snap.IsPresentMonRunning;
        FpsStatusText = PresentMonService.FormatStatusText(
            snap.StatusKind, snap.StatusErrorDetail, snap.ActiveGameName, snap.FrameGenSource);
        IsGameActive = snap.IsGameActive;
        _isFrameGenActive = snap.FrameGenFps.HasValue;

        _gpuTechDetectorService.UpdatePresentMonMetrics(snap);

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
        _lastGpuTechSnapshot = snap;
        GpuDetectionStatusText = GpuTechDetectionSnapshot.FormatNote(
            snap.StatusKind, snap.ForegroundExeName, snap.LoadedModuleCount, snap.ActiveRenderingGpuName);
        _isGpuTechGameActive = snap.IsGameActive;
        _lastAutoDetectedTechSources = new Dictionary<string, GpuTechDetectionSource>(
            snap.DetectedTechSources, StringComparer.OrdinalIgnoreCase);

        foreach (var gpu in DetectedGpus)
        {
            gpu.IsActiveRenderingGpu = snap.IsGameActive &&
                                       ((!string.IsNullOrEmpty(snap.ActiveRenderingGpuId) && gpu.Id == snap.ActiveRenderingGpuId) ||
                                        (string.IsNullOrEmpty(snap.ActiveRenderingGpuId) && snap.ActiveRenderingVendor == gpu.Vendor && gpu.IsDiscrete));
        }

        if (snap.IsGameActive && !string.IsNullOrWhiteSpace(snap.ForegroundExeName))
        {
            if (!string.Equals(_currentOverrideGameExe, snap.ForegroundExeName, StringComparison.OrdinalIgnoreCase))
                CurrentOverrideGameExe = snap.ForegroundExeName;
        }

        foreach (var group in GpuTechGroups)
        {
            foreach (var tech in group.Items)
            {
                if (snap.IsGameActive && _lastAutoDetectedTechSources.TryGetValue(tech.Id, out var src))
                {
                    tech.IsAutoDetected = true;
                    tech.AutoDetectionSource = src;
                }
                else
                {
                    tech.IsAutoDetected = false;
                    tech.AutoDetectionSource = GpuTechDetectionSource.None;
                }
            }
        }

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
                    _ => _isGpuTechGameActive && _lastAutoDetectedTechSources.ContainsKey(tech.Id)
                };

                if (!show) continue;

                Color baseColor = GetBadgeColor(tech.Vendor);
                var bg = Freeze(new SolidColorBrush(Color.FromArgb(46, baseColor.R, baseColor.G, baseColor.B)));
                var border = Freeze(new SolidColorBrush(Color.FromArgb(185, baseColor.R, baseColor.G, baseColor.B)));
                var text = Freeze(new SolidColorBrush(Color.FromArgb(255,
                    (byte)Math.Min(255, baseColor.R + 70),
                    (byte)Math.Min(255, baseColor.G + 70),
                    (byte)Math.Min(255, baseColor.B + 70))));

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

    private void UpdateFpsMetricsVisibility()
    {
        foreach (var item in AllMetrics)
        {
            if (item.Category != MetricCategory.Fps)
            {
                item.IsVisibleOnOverlay = true;
                continue;
            }

            if (item.IsGpuBadgesMetric)
            {
                item.IsVisibleOnOverlay = HasActiveGpuBadges || !AutoHideFpsWhenIdle;
                continue;
            }

            if (AutoHideFpsWhenIdle && !IsGameActive)
            {
                item.IsVisibleOnOverlay = false;
                continue;
            }

            if (item.Id == "fps_framegen" && AutoHideFrameGenWhenInactive && !_isFrameGenActive)
            {
                item.IsVisibleOnOverlay = false;
                continue;
            }

            item.IsVisibleOnOverlay = true;
        }
    }

    private void UpdateGpuVendorSupportStates()
    {
        var presentVendors = new HashSet<GpuVendor>(DetectedGpus.Select(g => g.Vendor));
        SupportedGpuTechItems.Clear();

        foreach (var group in GpuTechGroups)
        {
            bool supported = presentVendors.Contains(group.Vendor);
            group.IsSupported = supported;
            group.IsVisible = supported || !HideUnsupportedGpuTechs;

            if (supported)
            {
                var names = DetectedGpus.Where(g => g.Vendor == group.Vendor).Select(g => g.Name);
                group.SupportBadgeText = LocalizationService.Instance.Format("GpuAvailableBadge", string.Join(", ", names));

                foreach (var item in group.Items)
                    SupportedGpuTechItems.Add(item);
            }
            else
            {
                group.SupportBadgeText = LocalizationService.Instance["GpuVendorMissingBadge"];
            }
        }

        RebuildActiveGpuBadges();
    }

    private void RefreshMonitorsList(string? preferredDeviceName)
    {
        Monitors.Clear();
        foreach (var m in Win32Helper.EnumerateMonitors())
            Monitors.Add(m);

        _selectedMonitor = Monitors.FirstOrDefault(m =>
                               !string.IsNullOrWhiteSpace(preferredDeviceName) &&
                               m.DeviceName.Equals(preferredDeviceName, StringComparison.OrdinalIgnoreCase))
                           ?? Monitors.FirstOrDefault(m => m.IsPrimary)
                           ?? Monitors.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedMonitor));
    }

    private void RefreshAllLayerVisuals()
    {
        foreach (var layer in OverlayLayers)
            layer.RefreshVisuals();
    }

    private void RebuildOuterFrameVisuals()
    {
        if (!_showOuterFrame || _outerFrameBgOpacity <= 0.1)
        {
            _cachedOuterBgBrush = TransparentBrush;
        }
        else
        {
            var c = MetricItem.TryParseHex(_outerFrameBgColorHex, out var parsed) ? parsed : Color.FromRgb(8, 10, 14);
            byte a = (byte)Math.Clamp(Math.Round(_outerFrameBgOpacity / 100.0 * 255.0), 0, 255);
            _cachedOuterBgBrush = Freeze(new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)));
        }

        if (!_showOuterFrame || !_outerFrameBorderEnabled || _outerFrameBorderOpacity <= 0.1)
        {
            _cachedOuterBorderBrush = TransparentBrush;
        }
        else
        {
            var c = MetricItem.TryParseHex(_outerFrameBorderColorHex, out var parsed) ? parsed : Colors.White;
            byte a = (byte)Math.Clamp(Math.Round(_outerFrameBorderOpacity / 100.0 * 255.0), 0, 255);
            _cachedOuterBorderBrush = Freeze(new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)));
        }

        OnPropertyChanged(nameof(EffectiveOuterFrameBackground));
        OnPropertyChanged(nameof(EffectiveOuterFrameBorderBrush));
        OnPropertyChanged(nameof(EffectiveOuterFrameBorderThickness));
        OnPropertyChanged(nameof(EffectiveOuterFrameCornerRadius));
        OnPropertyChanged(nameof(EffectiveOuterFramePadding));
    }

    private void NotifyAnchorProperties()
    {
        OnPropertyChanged(nameof(IsAnchorTopLeft));
        OnPropertyChanged(nameof(IsAnchorTopCenter));
        OnPropertyChanged(nameof(IsAnchorTopRight));
        OnPropertyChanged(nameof(IsAnchorMiddleLeft));
        OnPropertyChanged(nameof(IsAnchorMiddleCenter));
        OnPropertyChanged(nameof(IsAnchorMiddleRight));
        OnPropertyChanged(nameof(IsAnchorBottomLeft));
        OnPropertyChanged(nameof(IsAnchorBottomCenter));
        OnPropertyChanged(nameof(IsAnchorBottomRight));
    }

    private void PopulateMetricsFromService()
    {
        AvailableGroups.Clear();
        AllMetrics.Clear();
        _metricsById.Clear();

        var groups = new Dictionary<string, MetricGroupViewModel>();
        foreach (var metric in _hardwareService.DiscoveredMetrics)
        {
            _metricsById[metric.Id] = metric;
            AllMetrics.Add(metric);

            if (!groups.TryGetValue(metric.GroupKey, out var groupVm))
            {
                var captured = metric;
                groupVm = new MetricGroupViewModel
                {
                    GroupKey = metric.GroupKey,
                    TitleProvider = () => captured.GroupTitle,
                    SubtitleProvider = () => captured.GroupSubtitle
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
            SubtitleKey = "GpuSubNvidia",
            AccentBrush = Freeze("#76B900")
        };
        AddTech(nvidiaGroup, "nv_dlss", "DLSS", "DLSS", "DescNvDlss");
        AddTech(nvidiaGroup, "nv_dlaa", "DLAA", "DLAA", "DescNvDlaa");
        AddTech(nvidiaGroup, "nv_dlss_fg", "DLSS Frame Generation", "DLSS FG", "DescNvDlssFg");
        AddTech(nvidiaGroup, "nv_smooth_motion", "Smooth Motion", "Smooth Motion", "DescNvSmoothMotion");
        AddTech(nvidiaGroup, "nv_rt", "Ray Tracing", "RT", "DescNvRt");
        AddTech(nvidiaGroup, "nv_pt", "Path Tracing", "Path Tracing", "DescNvPt");
        AddTech(nvidiaGroup, "nv_reflex", "NVIDIA Reflex", "Reflex", "DescNvReflex");
        GpuTechGroups.Add(nvidiaGroup);

        var amdGroup = new GpuTechVendorGroupViewModel
        {
            Vendor = GpuVendor.Amd,
            Title = "AMD",
            SubtitleKey = "GpuSubAmd",
            AccentBrush = Freeze("#EF4444")
        };
        AddTech(amdGroup, "amd_fsr", "AMD FSR", "FSR", "DescAmdFsr");
        AddTech(amdGroup, "amd_fg_afmf", "FSR Frame Generation & AFMF (Fluid Motion Frames)", "FSR FG / AFMF", "DescAmdFgAfmf");
        AddTech(amdGroup, "amd_latency", "FSR Latency Reduction", "Anti-Lag", "DescAmdLatency");
        GpuTechGroups.Add(amdGroup);

        var intelGroup = new GpuTechVendorGroupViewModel
        {
            Vendor = GpuVendor.Intel,
            Title = "Intel",
            SubtitleKey = "GpuSubIntel",
            AccentBrush = Freeze("#38BDF8")
        };
        AddTech(intelGroup, "intel_xess_sr", "XeSS Super Resolution", "XeSS", "DescIntelXessSr");
        AddTech(intelGroup, "intel_xess_fg", "XeSS Frame Generation / Multi-Frame Generation", "XeSS FG", "DescIntelXessFg");
        AddTech(intelGroup, "intel_xell", "Xe Low Latency (XeLL)", "XeLL", "DescIntelXell");
        AddTech(intelGroup, "intel_rt", "Ray Tracing", "RT", "DescIntelRt");
        GpuTechGroups.Add(intelGroup);
    }

    private void AddTech(GpuTechVendorGroupViewModel group, string id, string name, string badgeText, string descriptionKey)
    {
        group.Items.Add(new GpuTechItemViewModel
        {
            Id = id,
            Name = name,
            BadgeText = badgeText,
            DescriptionKey = descriptionKey,
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

            _overlayAnchor = cfg.OverlayAnchor;
            _overlayOffsetX = cfg.OverlayOffsetX;
            _overlayOffsetY = cfg.OverlayOffsetY;
            RefreshMonitorsList(cfg.MonitorDeviceName);
            _isWidgetTopmost = cfg.IsWidgetTopmost;
            _isWidgetVisible = cfg.IsWidgetVisible;
            _enableToggleOverlayHotkey = cfg.EnableToggleOverlayHotkey;

            _showEditorGrid = cfg.ShowEditorGrid;
            _snapToGrid = cfg.SnapToGrid;
            _gridSnapSize = cfg.GridSnapSize;
            _cellPaddingH = cfg.CellPaddingH;
            _cellPaddingV = cfg.CellPaddingV;
            _globalLabelValueGap = cfg.GlobalLabelValueGap;
            _globalContentAlignment = cfg.GlobalContentAlignment;

            _selectedFontFamily = InstalledFontFamilies.FirstOrDefault(f =>
                                      f.Source.Equals(cfg.FontFamilyName, StringComparison.OrdinalIgnoreCase))
                                  ?? InstalledFontFamilies.FirstOrDefault(f =>
                                      f.Source.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase))
                                  ?? InstalledFontFamilies.First();
            _fontSize = cfg.FontSize;
            _selectedFontWeightOption = FontWeightOptions.FirstOrDefault(w =>
                                            w.Key.Equals(cfg.FontWeightName, StringComparison.OrdinalIgnoreCase))
                                        ?? FontWeightOptions[4];
            _textColorHex = cfg.TextColorHex;
            _labelColorHex = cfg.LabelColorHex;
            _globalValueBrush = CreateFrozenBrush(_textColorHex, Colors.White);
            _globalLabelBrush = CreateFrozenBrush(_labelColorHex, Color.FromRgb(96, 205, 255));

            _defaultCellBgColorHex = cfg.DefaultCellBgColorHex;
            _defaultCellBgOpacity = cfg.DefaultCellBgOpacity;
            _defaultCellBorderEnabled = cfg.DefaultCellBorderEnabled;
            _defaultCellBorderColorHex = cfg.DefaultCellBorderColorHex;
            _defaultCellBorderOpacity = cfg.DefaultCellBorderOpacity;
            _defaultCellBorderThickness = cfg.DefaultCellBorderThickness;
            _defaultCellCornerRadius = cfg.DefaultCellCornerRadius;

            _showOuterFrame = cfg.ShowOuterFrame;
            _outerFrameBgColorHex = cfg.OuterFrameBgColorHex;
            _outerFrameBgOpacity = cfg.OuterFrameBgOpacity;
            _outerFrameBorderEnabled = cfg.OuterFrameBorderEnabled;
            _outerFrameBorderColorHex = cfg.OuterFrameBorderColorHex;
            _outerFrameBorderOpacity = cfg.OuterFrameBorderOpacity;
            _outerFrameBorderThickness = cfg.OuterFrameBorderThickness;
            _outerFrameCornerRadius = cfg.OuterFrameCornerRadius;
            _outerFramePadding = cfg.OuterFramePadding;

            _autoHideFpsWhenIdle = cfg.AutoHideFpsWhenIdle;
            _autoHideFrameGenWhenInactive = cfg.AutoHideFrameGenWhenInactive;
            _fpsGraphHeight = cfg.FpsGraphHeight;
            _fpsGraphBaseColorHex = cfg.FpsGraphBaseColorHex;
            _fpsGraphFrameGenColorHex = cfg.FpsGraphFrameGenColorHex;

            _runAtWindowsStartup = Win32Helper.IsWindowsStartupEnabled() || cfg.RunAtWindowsStartup;
            _minimizeToTrayOnClose = cfg.MinimizeToTrayOnClose;
            _autoCheckUpdatesOnStartup = cfg.AutoCheckUpdatesOnStartup;

            // Nạp các lớp Canvas
            OverlayLayers.Clear();
            if (cfg.OverlayLayers is { Count: > 0 })
            {
                foreach (var lc in cfg.OverlayLayers)
                {
                    MetricItem? metric = null;
                    if (lc.Kind == OverlayLayerKind.Metric)
                    {
                        if (string.IsNullOrWhiteSpace(lc.MetricId) || !_metricsById.TryGetValue(lc.MetricId, out metric))
                            continue;
                    }

                    var layer = new OverlayLayerViewModel(this, lc.Kind, metric, lc.X, lc.Y, lc.Width, lc.Height, lc.ZIndex, lc.LayerId);
                    if (lc.Style != null) layer.ApplyStyleConfig(lc.Style);
                    OverlayLayers.Add(layer);
                }
            }

            if (OverlayLayers.Count == 0)
                ApplyPresetPill();
            else
            {
                SyncMetricPlacedFlags();
                SelectedLayer = OverlayLayers.FirstOrDefault();
            }

            _hideUnsupportedGpuTechs = cfg.HideUnsupportedGpuTechs;
            _useCustomGpuBadgeColor = cfg.UseCustomGpuBadgeColor;
            _customGpuBadgeColorHex = cfg.CustomGpuBadgeColorHex;

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

    public void ResetToDefaults()
    {
        _isRestoring = true;
        try
        {
            OverlayAnchor = OverlayAnchor.TopLeft;
            OverlayOffsetX = 16;
            OverlayOffsetY = 16;
            RefreshMonitorsList(null);
            IsWidgetTopmost = true;
            IsWidgetVisible = true;
            EnableToggleOverlayHotkey = true;

            ShowEditorGrid = true;
            SnapToGrid = true;
            GridSnapSize = 8;

            SelectedFontFamily = InstalledFontFamilies.FirstOrDefault(f => f.Source.Equals("Segoe UI", StringComparison.OrdinalIgnoreCase))
                                 ?? InstalledFontFamilies.First();
            FontSize = 13;
            SelectedFontWeightOption = FontWeightOptions[4];

            AutoHideFpsWhenIdle = false;
            AutoHideFrameGenWhenInactive = false;
            FpsGraphHeight = 46;
            FpsGraphBaseColorHex = "#4ADE80";
            FpsGraphFrameGenColorHex = "#60CDFF";
            MinimizeToTrayOnClose = true;
            AutoCheckUpdatesOnStartup = true;

            HideUnsupportedGpuTechs = false;
            UseCustomGpuBadgeColor = false;
            CustomGpuBadgeColorHex = "#60CDFF";

            foreach (var group in GpuTechGroups)
                foreach (var tech in group.Items)
                    tech.SetEnabledSilent(true);
        }
        finally
        {
            _isRestoring = false;
        }

        ApplyPresetPill();
        UpdateGpuVendorSupportStates();
        RebuildActiveGpuBadges();
        UpdateFpsMetricsVisibility();
        OverlayPositionChanged?.Invoke();
    }

    public AppConfig BuildConfigSnapshot()
    {
        var layers = OverlayLayers
            .Select(l => new OverlayLayerConfig
            {
                LayerId = l.LayerId,
                Kind = l.Kind,
                MetricId = l.IsMetricLayer ? l.MetricId : null,
                X = l.X,
                Y = l.Y,
                Width = l.Width,
                Height = l.Height,
                ZIndex = l.ZIndex,
                Style = l.ExportStyleConfig()
            })
            .ToList();

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
            ConfigVersion = 3,
            LanguageCode = _selectedLanguage.Code,
            OverlayAnchor = _overlayAnchor,
            OverlayOffsetX = _overlayOffsetX,
            OverlayOffsetY = _overlayOffsetY,
            MonitorDeviceName = _selectedMonitor?.DeviceName,
            IsWidgetTopmost = _isWidgetTopmost,
            IsWidgetVisible = _isWidgetVisible,
            EnableToggleOverlayHotkey = _enableToggleOverlayHotkey,
            ShowEditorGrid = _showEditorGrid,
            SnapToGrid = _snapToGrid,
            GridSnapSize = _gridSnapSize,
            CellPaddingH = _cellPaddingH,
            CellPaddingV = _cellPaddingV,
            GlobalLabelValueGap = _globalLabelValueGap,
            GlobalContentAlignment = _globalContentAlignment,
            OverlayLayers = layers,
            FontFamilyName = _selectedFontFamily.Source,
            FontSize = _fontSize,
            FontWeightName = _selectedFontWeightOption.Key,
            TextColorHex = _textColorHex,
            LabelColorHex = _labelColorHex,
            DefaultCellBgColorHex = _defaultCellBgColorHex,
            DefaultCellBgOpacity = _defaultCellBgOpacity,
            DefaultCellBorderEnabled = _defaultCellBorderEnabled,
            DefaultCellBorderColorHex = _defaultCellBorderColorHex,
            DefaultCellBorderOpacity = _defaultCellBorderOpacity,
            DefaultCellBorderThickness = _defaultCellBorderThickness,
            DefaultCellCornerRadius = _defaultCellCornerRadius,
            ShowOuterFrame = _showOuterFrame,
            OuterFrameBgColorHex = _outerFrameBgColorHex,
            OuterFrameBgOpacity = _outerFrameBgOpacity,
            OuterFrameBorderEnabled = _outerFrameBorderEnabled,
            OuterFrameBorderColorHex = _outerFrameBorderColorHex,
            OuterFrameBorderOpacity = _outerFrameBorderOpacity,
            OuterFrameBorderThickness = _outerFrameBorderThickness,
            OuterFrameCornerRadius = _outerFrameCornerRadius,
            OuterFramePadding = _outerFramePadding,
            AutoHideFpsWhenIdle = _autoHideFpsWhenIdle,
            AutoHideFrameGenWhenInactive = _autoHideFrameGenWhenInactive,
            FpsGraphHeight = _fpsGraphHeight,
            FpsGraphBaseColorHex = _fpsGraphBaseColorHex,
            FpsGraphFrameGenColorHex = _fpsGraphFrameGenColorHex,
            RunAtWindowsStartup = _runAtWindowsStartup,
            MinimizeToTrayOnClose = _minimizeToTrayOnClose,
            AutoCheckUpdatesOnStartup = _autoCheckUpdatesOnStartup,
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

    private static string NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string s = value.Trim();
        if (!s.StartsWith('#')) s = "#" + s;
        return s.ToUpperInvariant();
    }

    private static ColorPreset MakePreset(string nameKey, string hex)
    {
        MetricItem.TryParseHex(hex, out var color);
        return new ColorPreset { NameKey = nameKey, Hex = hex, Brush = Freeze(new SolidColorBrush(color)) };
    }

    private static SolidColorBrush CreateFrozenBrush(string hex, Color fallback)
    {
        var c = MetricItem.TryParseHex(hex, out var parsed) ? parsed : fallback;
        return Freeze(new SolidColorBrush(c));
    }

    private static SolidColorBrush Freeze(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex);
        return Freeze(new SolidColorBrush(c));
    }

    private static SolidColorBrush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
