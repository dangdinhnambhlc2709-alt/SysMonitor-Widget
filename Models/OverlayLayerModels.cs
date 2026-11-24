using System;
using System.Windows;
using System.Windows.Media;
using WinHardwareMonitor.Services;
using WinHardwareMonitor.ViewModels;
using Wpf.Ui.Controls;

namespace WinHardwareMonitor.Models;

public sealed class MonitorOption : ObservableObject
{
    public string DeviceName { get; init; } = string.Empty;
    public int Index { get; init; }
    public int WidthPx { get; init; }
    public int HeightPx { get; init; }
    public bool IsPrimary { get; init; }

    public string DisplayName => IsPrimary
        ? LocalizationService.Instance.Format("MonitorPrimaryFormat", Index + 1, WidthPx, HeightPx)
        : LocalizationService.Instance.Format("MonitorSecondaryFormat", Index + 1, WidthPx, HeightPx);

    public void RefreshLocalization() => OnPropertyChanged(nameof(DisplayName));
}

public sealed class AlignmentOption : ObservableObject
{
    public CellContentAlignment Alignment { get; }
    public string DisplayKey { get; }
    public string DisplayName => LocalizationService.Instance[DisplayKey];

    public AlignmentOption(CellContentAlignment alignment, string displayKey)
    {
        Alignment = alignment;
        DisplayKey = displayKey;
    }

    public void RefreshLocalization() => OnPropertyChanged(nameof(DisplayName));
}

public sealed class DisplayModeOption : ObservableObject
{
    public LayerDisplayMode Mode { get; }
    public string DisplayKey { get; }
    public string DisplayName => LocalizationService.Instance[DisplayKey];

    public DisplayModeOption(LayerDisplayMode mode, string displayKey)
    {
        Mode = mode;
        DisplayKey = displayKey;
    }

    public void RefreshLocalization() => OnPropertyChanged(nameof(DisplayName));
}

/// <summary>
/// Một lớp (Layer) trên bảng thiết kế Canvas kiểu RTSS Overlay Editor.
/// Hỗ trợ 3 loại lớp: Metric (Thông số / Đồ thị / Huy hiệu GPU), StaticText (Nhãn chữ tự do), BackdropBox (Khối nền/khung Pill).
/// Hiển thị WYSIWYG giống hệt 100% giữa Canvas thiết kế và cửa sổ OSD Overlay ngoài màn hình.
/// </summary>
public sealed class OverlayLayerViewModel : ObservableObject
{
    private static readonly SolidColorBrush TransparentBrush = Freeze(new SolidColorBrush(Colors.Transparent));

    private readonly MainViewModel _owner;
    private double _x;
    private double _y;
    private double _width;  // 0 = tự co theo nội dung
    private double _height; // 0 = tự co theo nội dung
    private int _zIndex;
    private bool _isSelected;

    // Nội dung & Kiểu hiển thị
    private bool _useGlobalStyle = true;
    private string _customLabel = string.Empty;
    private LayerDisplayMode _displayMode = LayerDisplayMode.LabelAndValue;
    private MetricHeaderStyle _headerStyle = MetricHeaderStyle.Text;
    private string _customSymbolName = string.Empty;
    private string _labelColorHex = string.Empty;
    private string _valueColorHex = string.Empty;
    private bool _useCustomFontSize;
    private double _customFontSize = 13;
    private bool _useCustomGap;
    private double _customLabelValueGap = 8;
    private bool _useCustomAlignment;
    private CellContentAlignment _customContentAlignment = CellContentAlignment.InlineWithGap;
    private bool _useCustomPadding;
    private double _customPaddingH = 8;
    private double _customPaddingV = 4;

    // Kiểu dáng phẳng riêng của lớp (khi UseGlobalStyle = false)
    private string _bgColorHex = "#0A0C10";
    private double _bgOpacity = 78;
    private bool _borderEnabled = true;
    private string _borderColorHex = "#FFFFFF";
    private double _borderOpacity = 22;
    private double _borderThickness = 1.0;
    private double _cornerRadius;

    // Brush cache tránh cấp phát lại mỗi khung hình
    private SolidColorBrush _cachedBgBrush = TransparentBrush;
    private SolidColorBrush _cachedBorderBrush = TransparentBrush;
    private SolidColorBrush _cachedLabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(96, 205, 255)));
    private SolidColorBrush _cachedValueBrush = Freeze(new SolidColorBrush(Colors.White));

    public string LayerId { get; }
    public OverlayLayerKind Kind { get; }
    public MetricItem? Metric { get; }
    public string MetricId => Metric?.Id ?? string.Empty;

    public bool IsMetricLayer => Kind == OverlayLayerKind.Metric && Metric != null;
    public bool IsStaticTextLayer => Kind == OverlayLayerKind.StaticText;
    public bool IsBackdropBoxLayer => Kind == OverlayLayerKind.BackdropBox;
    public bool IsGraphMetric => IsMetricLayer && Metric!.IsGraphMetric;
    public bool IsGpuBadgesMetric => IsMetricLayer && Metric!.IsGpuBadgesMetric;
    public bool IsStandardMetric => IsMetricLayer && !Metric!.IsGraphMetric && !Metric.IsGpuBadgesMetric;
    public bool SupportsTextContent => IsMetricLayer || IsStaticTextLayer;

    public OverlayLayerViewModel(
        MainViewModel owner,
        OverlayLayerKind kind,
        MetricItem? metric,
        double x,
        double y,
        double width = 0,
        double height = 0,
        int zIndex = 10,
        string? layerId = null)
    {
        _owner = owner;
        Kind = kind;
        Metric = metric;
        LayerId = string.IsNullOrWhiteSpace(layerId) ? Guid.NewGuid().ToString("N")[..8] : layerId;
        _x = Math.Max(0, Math.Round(x));
        _y = Math.Max(0, Math.Round(y));
        _width = Math.Max(0, Math.Round(width));
        _height = Math.Max(0, Math.Round(height));
        _zIndex = zIndex;

        _customFontSize = owner.FontSize;
        _customLabelValueGap = owner.GlobalLabelValueGap;
        _customContentAlignment = owner.GlobalContentAlignment;
        _customPaddingH = owner.CellPaddingH;
        _customPaddingV = owner.CellPaddingV;
        _bgColorHex = owner.DefaultCellBgColorHex;
        _bgOpacity = owner.DefaultCellBgOpacity;
        _borderEnabled = owner.DefaultCellBorderEnabled;
        _borderColorHex = owner.DefaultCellBorderColorHex;
        _borderOpacity = owner.DefaultCellBorderOpacity;
        _borderThickness = owner.DefaultCellBorderThickness;
        _cornerRadius = owner.DefaultCellCornerRadius;

        if (kind == OverlayLayerKind.StaticText)
        {
            _customLabel = "LABEL";
            _displayMode = LayerDisplayMode.LabelOnly;
        }
        else if (kind == OverlayLayerKind.BackdropBox)
        {
            _useGlobalStyle = false;
            _bgColorHex = "#080A0E";
            _bgOpacity = 82;
            _borderEnabled = true;
            _borderColorHex = "#FFFFFF";
            _borderOpacity = 28;
            _borderThickness = 1.0;
            _cornerRadius = 4;
            if (_width <= 0) _width = 240;
            if (_height <= 0) _height = 36;
        }

        if (Metric != null)
        {
            Metric.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(MetricItem.ShortLabel) or nameof(MetricItem.Title))
                {
                    OnPropertyChanged(nameof(DisplayLabel));
                    OnPropertyChanged(nameof(LayerTitle));
                }
                else if (args.PropertyName == nameof(MetricItem.IsVisibleOnOverlay))
                {
                    OnPropertyChanged(nameof(IsVisibleOnOverlay));
                }
            };
        }

        RebuildCachedBrushes();
    }

    public bool IsVisibleOnOverlay => Metric == null || Metric.IsVisibleOnOverlay;

    public string LayerTitle => Kind switch
    {
        OverlayLayerKind.StaticText => string.IsNullOrWhiteSpace(_customLabel)
            ? LocalizationService.Instance["LayerKindText"]
            : _customLabel.Trim(),
        OverlayLayerKind.BackdropBox => LocalizationService.Instance["LayerKindBox"],
        _ => Metric?.Title ?? string.Empty
    };

    public double X
    {
        get => _x;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 1600);
            if (SetProperty(ref _x, clamped))
            {
                OnPropertyChanged(nameof(GeometrySummary));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public double Y
    {
        get => _y;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 1000);
            if (SetProperty(ref _y, clamped))
            {
                OnPropertyChanged(nameof(GeometrySummary));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public double Width
    {
        get => _width;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 1400);
            if (SetProperty(ref _width, clamped))
            {
                OnPropertyChanged(nameof(RenderWidth));
                OnPropertyChanged(nameof(WidthLabel));
                OnPropertyChanged(nameof(GeometrySummary));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public double Height
    {
        get => _height;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 800);
            if (SetProperty(ref _height, clamped))
            {
                OnPropertyChanged(nameof(RenderHeight));
                OnPropertyChanged(nameof(EffectiveGraphHeight));
                OnPropertyChanged(nameof(HeightLabel));
                OnPropertyChanged(nameof(GeometrySummary));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public double RenderWidth => _width > 0 ? _width : double.NaN;
    public double RenderHeight => _height > 0 ? _height : double.NaN;
    public double EffectiveGraphHeight => _height > 26 ? Math.Max(20, _height - 24) : _owner.FpsGraphHeight;

    public string WidthLabel => _width <= 0 ? LocalizationService.Instance["SizeAutoShort"] : $"{_width:F0} px";
    public string HeightLabel => _height <= 0 ? LocalizationService.Instance["SizeAutoShort"] : $"{_height:F0} px";
    public string GeometrySummary => $"X:{X:F0}  Y:{Y:F0}  •  W:{WidthLabel}  H:{HeightLabel}  •  Z:{ZIndex}";

    public int ZIndex
    {
        get => _zIndex;
        set
        {
            if (SetProperty(ref _zIndex, value))
            {
                OnPropertyChanged(nameof(GeometrySummary));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool UseGlobalStyle
    {
        get => _useGlobalStyle;
        set
        {
            if (SetProperty(ref _useGlobalStyle, value))
            {
                OnPropertyChanged(nameof(UseCustomBoxStyle));
                RefreshVisuals();
                _owner.NotifyLayerEdited();
            }
        }
    }

    public bool UseCustomBoxStyle => !_useGlobalStyle;

    public string CustomLabel
    {
        get => _customLabel;
        set
        {
            string norm = value ?? string.Empty;
            if (SetProperty(ref _customLabel, norm))
            {
                OnPropertyChanged(nameof(DisplayLabel));
                OnPropertyChanged(nameof(LayerTitle));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public string DisplayLabel => string.IsNullOrWhiteSpace(_customLabel)
        ? (Metric?.ShortLabel ?? string.Empty)
        : _customLabel.Trim();

    public LayerDisplayMode DisplayMode
    {
        get => _displayMode;
        set
        {
            if (SetProperty(ref _displayMode, value))
            {
                NotifyDisplayModeProperties();
                _owner.NotifyLayerEdited();
            }
        }
    }

    public bool ShowHeaderPart => _displayMode is LayerDisplayMode.LabelAndValue or LayerDisplayMode.LabelOnly;
    public bool ShowValuePart => IsMetricLayer && _displayMode is LayerDisplayMode.LabelAndValue or LayerDisplayMode.ValueOnly;
    public bool ShowIconPart => ShowHeaderPart && IsIconHeader && !IsStaticTextLayer;
    public bool ShowTextLabelPart => ShowHeaderPart && (IsTextHeader || IsStaticTextLayer);

    public MetricHeaderStyle HeaderStyle
    {
        get => _headerStyle;
        set
        {
            if (SetProperty(ref _headerStyle, value))
            {
                OnPropertyChanged(nameof(IsTextHeader));
                OnPropertyChanged(nameof(IsIconHeader));
                NotifyDisplayModeProperties();
                _owner.NotifyLayerEdited();
            }
        }
    }

    public bool IsTextHeader
    {
        get => _headerStyle == MetricHeaderStyle.Text;
        set { if (value) HeaderStyle = MetricHeaderStyle.Text; }
    }

    public bool IsIconHeader
    {
        get => _headerStyle == MetricHeaderStyle.Icon;
        set { if (value) HeaderStyle = MetricHeaderStyle.Icon; }
    }

    public string CustomSymbolName
    {
        get => _customSymbolName;
        set
        {
            string norm = value?.Trim() ?? string.Empty;
            if (SetProperty(ref _customSymbolName, norm))
            {
                OnPropertyChanged(nameof(EffectiveSymbol));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public SymbolRegular EffectiveSymbol =>
        !string.IsNullOrWhiteSpace(_customSymbolName) && Enum.TryParse<SymbolRegular>(_customSymbolName, true, out var parsed)
            ? parsed
            : (Metric?.DefaultSymbol ?? SymbolRegular.Tag24);

    public string LabelColorHex
    {
        get => _labelColorHex;
        set
        {
            string norm = NormalizeHex(value);
            if (SetProperty(ref _labelColorHex, norm))
            {
                RebuildCachedBrushes();
                OnPropertyChanged(nameof(EffectiveLabelBrush));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public string ValueColorHex
    {
        get => _valueColorHex;
        set
        {
            string norm = NormalizeHex(value);
            if (SetProperty(ref _valueColorHex, norm))
            {
                RebuildCachedBrushes();
                OnPropertyChanged(nameof(EffectiveValueBrush));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public bool UseCustomFontSize
    {
        get => _useCustomFontSize;
        set
        {
            if (SetProperty(ref _useCustomFontSize, value))
            {
                NotifyFontProperties();
                _owner.NotifyLayerEdited();
            }
        }
    }

    public double CustomFontSize
    {
        get => _customFontSize;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 10, 36);
            if (SetProperty(ref _customFontSize, clamped))
            {
                OnPropertyChanged(nameof(CustomFontSizeLabel));
                NotifyFontProperties();
                _owner.NotifyLayerEdited();
            }
        }
    }

    public string CustomFontSizeLabel => $"{CustomFontSize:F0} px";
    public double EffectiveFontSize => _useCustomFontSize ? _customFontSize : _owner.FontSize;
    public double EffectiveLabelFontSize => IsStaticTextLayer
        ? EffectiveFontSize
        : Math.Max(9, Math.Round(EffectiveFontSize * 0.90, 1));
    public double EffectiveIconFontSize => Math.Max(11, Math.Round(EffectiveFontSize * 1.10, 1));

    public bool UseCustomGap
    {
        get => _useCustomGap;
        set
        {
            if (SetProperty(ref _useCustomGap, value))
            {
                NotifyAlignmentProperties();
                _owner.NotifyLayerEdited();
            }
        }
    }

    public double CustomLabelValueGap
    {
        get => _customLabelValueGap;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 60);
            if (SetProperty(ref _customLabelValueGap, clamped))
            {
                OnPropertyChanged(nameof(CustomGapLabel));
                NotifyAlignmentProperties();
                _owner.NotifyLayerEdited();
            }
        }
    }

    public string CustomGapLabel => $"{CustomLabelValueGap:F0} px";

    public bool UseCustomAlignment
    {
        get => _useCustomAlignment;
        set
        {
            if (SetProperty(ref _useCustomAlignment, value))
            {
                NotifyAlignmentProperties();
                _owner.NotifyLayerEdited();
            }
        }
    }

    public CellContentAlignment CustomContentAlignment
    {
        get => _customContentAlignment;
        set
        {
            if (SetProperty(ref _customContentAlignment, value))
            {
                NotifyAlignmentProperties();
                _owner.NotifyLayerEdited();
            }
        }
    }

    public bool UseCustomPadding
    {
        get => _useCustomPadding;
        set
        {
            if (SetProperty(ref _useCustomPadding, value))
            {
                OnPropertyChanged(nameof(EffectivePadding));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public double CustomPaddingH
    {
        get => _customPaddingH;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 24);
            if (SetProperty(ref _customPaddingH, clamped))
            {
                OnPropertyChanged(nameof(CustomPaddingHLabel));
                OnPropertyChanged(nameof(EffectivePadding));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public string CustomPaddingHLabel => $"{CustomPaddingH:F0} px";

    public double CustomPaddingV
    {
        get => _customPaddingV;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 20);
            if (SetProperty(ref _customPaddingV, clamped))
            {
                OnPropertyChanged(nameof(CustomPaddingVLabel));
                OnPropertyChanged(nameof(EffectivePadding));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public string CustomPaddingVLabel => $"{CustomPaddingV:F0} px";

    public double EffectiveLabelValueGap => _useCustomGap ? _customLabelValueGap : _owner.GlobalLabelValueGap;
    public CellContentAlignment EffectiveAlignment => _useCustomAlignment ? _customContentAlignment : _owner.GlobalContentAlignment;

    public bool IsInlineLayout => EffectiveAlignment == CellContentAlignment.InlineWithGap;
    public bool IsSpaceBetweenLayout => EffectiveAlignment == CellContentAlignment.SpaceBetween;
    public bool IsStackedLayout => EffectiveAlignment == CellContentAlignment.StackedVertical;
    public Thickness EffectiveInlineGapMargin => ShowValuePart ? new Thickness(0, 0, EffectiveLabelValueGap, 0) : new Thickness(0);
    public Thickness EffectiveStackedGapMargin => ShowValuePart ? new Thickness(0, 0, 0, Math.Min(EffectiveLabelValueGap, 24)) : new Thickness(0);

    // --- Kiểu dáng hộp phẳng riêng của lớp ---
    public string BgColorHex
    {
        get => _bgColorHex;
        set
        {
            string norm = NormalizeHex(value);
            if (SetProperty(ref _bgColorHex, norm) && MetricItem.TryParseHex(norm, out _))
            {
                RebuildCachedBrushes();
                OnPropertyChanged(nameof(EffectiveBackgroundBrush));
                OnPropertyChanged(nameof(BgPreviewBrush));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public double BgOpacity
    {
        get => _bgOpacity;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 100);
            if (SetProperty(ref _bgOpacity, clamped))
            {
                OnPropertyChanged(nameof(BgOpacityLabel));
                RebuildCachedBrushes();
                OnPropertyChanged(nameof(EffectiveBackgroundBrush));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public string BgOpacityLabel => $"{BgOpacity:F0}%";

    public bool BorderEnabled
    {
        get => _borderEnabled;
        set
        {
            if (SetProperty(ref _borderEnabled, value))
            {
                RebuildCachedBrushes();
                OnPropertyChanged(nameof(EffectiveBorderBrush));
                OnPropertyChanged(nameof(EffectiveBorderThickness));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public string BorderColorHex
    {
        get => _borderColorHex;
        set
        {
            string norm = NormalizeHex(value);
            if (SetProperty(ref _borderColorHex, norm) && MetricItem.TryParseHex(norm, out _))
            {
                RebuildCachedBrushes();
                OnPropertyChanged(nameof(EffectiveBorderBrush));
                OnPropertyChanged(nameof(BorderPreviewBrush));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public double BorderOpacity
    {
        get => _borderOpacity;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 100);
            if (SetProperty(ref _borderOpacity, clamped))
            {
                OnPropertyChanged(nameof(BorderOpacityLabel));
                RebuildCachedBrushes();
                OnPropertyChanged(nameof(EffectiveBorderBrush));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public string BorderOpacityLabel => $"{BorderOpacity:F0}%";

    public double BorderThicknessValue
    {
        get => _borderThickness;
        set
        {
            double clamped = Math.Clamp(Math.Round(value * 2.0) / 2.0, 0.5, 4.0);
            if (SetProperty(ref _borderThickness, clamped))
            {
                OnPropertyChanged(nameof(BorderThicknessLabel));
                OnPropertyChanged(nameof(EffectiveBorderThickness));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public string BorderThicknessLabel => $"{BorderThicknessValue:F1} px";

    public double CornerRadiusValue
    {
        get => _cornerRadius;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 20);
            if (SetProperty(ref _cornerRadius, clamped))
            {
                OnPropertyChanged(nameof(CornerRadiusLabel));
                OnPropertyChanged(nameof(EffectiveCornerRadius));
                _owner.NotifyLayerEdited();
            }
        }
    }

    public string CornerRadiusLabel => $"{CornerRadiusValue:F0} px";

    public SolidColorBrush EffectiveBackgroundBrush => _cachedBgBrush;
    public SolidColorBrush EffectiveBorderBrush => _cachedBorderBrush;
    public SolidColorBrush EffectiveLabelBrush => _cachedLabelBrush;
    public SolidColorBrush EffectiveValueBrush => _cachedValueBrush;
    public SolidColorBrush BgPreviewBrush => CreateSolid(_bgColorHex, Color.FromRgb(10, 12, 16));
    public SolidColorBrush BorderPreviewBrush => CreateSolid(_borderColorHex, Colors.White);

    public Thickness EffectiveBorderThickness
    {
        get
        {
            bool enabled = _useGlobalStyle ? _owner.DefaultCellBorderEnabled : _borderEnabled;
            double thick = _useGlobalStyle ? _owner.DefaultCellBorderThickness : _borderThickness;
            return enabled ? new Thickness(thick) : new Thickness(0);
        }
    }

    public CornerRadius EffectiveCornerRadius =>
        new(_useGlobalStyle ? _owner.DefaultCellCornerRadius : _cornerRadius);

    public Thickness EffectivePadding => IsBackdropBoxLayer
        ? new Thickness(0)
        : (_useCustomPadding ? new Thickness(_customPaddingH, _customPaddingV, _customPaddingH, _customPaddingV) : _owner.EffectiveCellPadding);

    public void RefreshVisuals()
    {
        RebuildCachedBrushes();
        OnPropertyChanged(nameof(EffectiveBackgroundBrush));
        OnPropertyChanged(nameof(EffectiveBorderBrush));
        OnPropertyChanged(nameof(EffectiveBorderThickness));
        OnPropertyChanged(nameof(EffectiveCornerRadius));
        OnPropertyChanged(nameof(EffectiveLabelBrush));
        OnPropertyChanged(nameof(EffectiveValueBrush));
        OnPropertyChanged(nameof(EffectivePadding));
        OnPropertyChanged(nameof(EffectiveGraphHeight));
        NotifyFontProperties();
        NotifyAlignmentProperties();
        NotifyDisplayModeProperties();
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(LayerTitle));
        OnPropertyChanged(nameof(WidthLabel));
        OnPropertyChanged(nameof(HeightLabel));
        OnPropertyChanged(nameof(GeometrySummary));
    }

    public void ApplyStyleConfig(CellStyleConfig? style)
    {
        if (style == null) return;

        _useGlobalStyle = style.UseGlobalStyle;
        _customLabel = style.CustomLabel ?? string.Empty;
        _displayMode = style.DisplayMode;
        _headerStyle = style.HeaderStyle;
        _customSymbolName = style.CustomSymbolName ?? string.Empty;
        _labelColorHex = NormalizeHex(style.LabelColorHex);
        _valueColorHex = NormalizeHex(style.ValueColorHex);

        _useCustomFontSize = style.FontSizeOverride.HasValue;
        if (style.FontSizeOverride.HasValue)
            _customFontSize = Math.Clamp(style.FontSizeOverride.Value, 10, 36);

        _useCustomGap = style.LabelValueGapOverride.HasValue;
        if (style.LabelValueGapOverride.HasValue)
            _customLabelValueGap = Math.Clamp(style.LabelValueGapOverride.Value, 0, 60);

        _useCustomAlignment = style.ContentAlignmentOverride.HasValue;
        if (style.ContentAlignmentOverride.HasValue)
            _customContentAlignment = style.ContentAlignmentOverride.Value;

        _useCustomPadding = style.PaddingHOverride.HasValue || style.PaddingVOverride.HasValue;
        if (style.PaddingHOverride.HasValue) _customPaddingH = Math.Clamp(style.PaddingHOverride.Value, 0, 24);
        if (style.PaddingVOverride.HasValue) _customPaddingV = Math.Clamp(style.PaddingVOverride.Value, 0, 20);

        if (!string.IsNullOrWhiteSpace(style.BackgroundColorHex))
            _bgColorHex = NormalizeHex(style.BackgroundColorHex);
        if (style.BackgroundOpacity.HasValue)
            _bgOpacity = Math.Clamp(style.BackgroundOpacity.Value, 0, 100);
        if (style.BorderEnabled.HasValue)
            _borderEnabled = style.BorderEnabled.Value;
        if (!string.IsNullOrWhiteSpace(style.BorderColorHex))
            _borderColorHex = NormalizeHex(style.BorderColorHex);
        if (style.BorderOpacity.HasValue)
            _borderOpacity = Math.Clamp(style.BorderOpacity.Value, 0, 100);
        if (style.BorderThickness.HasValue)
            _borderThickness = Math.Clamp(style.BorderThickness.Value, 0.5, 4.0);
        if (style.CornerRadius.HasValue)
            _cornerRadius = Math.Clamp(style.CornerRadius.Value, 0, 20);

        RefreshVisuals();
        OnPropertyChanged(nameof(UseGlobalStyle));
        OnPropertyChanged(nameof(UseCustomBoxStyle));
        OnPropertyChanged(nameof(CustomLabel));
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(LayerTitle));
        OnPropertyChanged(nameof(DisplayMode));
        OnPropertyChanged(nameof(HeaderStyle));
        OnPropertyChanged(nameof(IsTextHeader));
        OnPropertyChanged(nameof(IsIconHeader));
        OnPropertyChanged(nameof(CustomSymbolName));
        OnPropertyChanged(nameof(EffectiveSymbol));
        OnPropertyChanged(nameof(LabelColorHex));
        OnPropertyChanged(nameof(ValueColorHex));
        OnPropertyChanged(nameof(UseCustomFontSize));
        OnPropertyChanged(nameof(CustomFontSize));
        OnPropertyChanged(nameof(CustomFontSizeLabel));
        OnPropertyChanged(nameof(UseCustomGap));
        OnPropertyChanged(nameof(CustomLabelValueGap));
        OnPropertyChanged(nameof(CustomGapLabel));
        OnPropertyChanged(nameof(UseCustomAlignment));
        OnPropertyChanged(nameof(CustomContentAlignment));
        OnPropertyChanged(nameof(UseCustomPadding));
        OnPropertyChanged(nameof(CustomPaddingH));
        OnPropertyChanged(nameof(CustomPaddingHLabel));
        OnPropertyChanged(nameof(CustomPaddingV));
        OnPropertyChanged(nameof(CustomPaddingVLabel));
        OnPropertyChanged(nameof(BgColorHex));
        OnPropertyChanged(nameof(BgOpacity));
        OnPropertyChanged(nameof(BgOpacityLabel));
        OnPropertyChanged(nameof(BgPreviewBrush));
        OnPropertyChanged(nameof(BorderEnabled));
        OnPropertyChanged(nameof(BorderColorHex));
        OnPropertyChanged(nameof(BorderOpacity));
        OnPropertyChanged(nameof(BorderOpacityLabel));
        OnPropertyChanged(nameof(BorderPreviewBrush));
        OnPropertyChanged(nameof(BorderThicknessValue));
        OnPropertyChanged(nameof(BorderThicknessLabel));
        OnPropertyChanged(nameof(CornerRadiusValue));
        OnPropertyChanged(nameof(CornerRadiusLabel));
    }

    public CellStyleConfig ExportStyleConfig() => new()
    {
        UseGlobalStyle = _useGlobalStyle,
        CustomLabel = string.IsNullOrWhiteSpace(_customLabel) ? null : _customLabel.Trim(),
        DisplayMode = _displayMode,
        HeaderStyle = _headerStyle,
        CustomSymbolName = string.IsNullOrWhiteSpace(_customSymbolName) ? null : _customSymbolName,
        LabelColorHex = MetricItem.TryParseHex(_labelColorHex, out _) ? _labelColorHex : null,
        ValueColorHex = MetricItem.TryParseHex(_valueColorHex, out _) ? _valueColorHex : null,
        FontSizeOverride = _useCustomFontSize ? _customFontSize : null,
        LabelValueGapOverride = _useCustomGap ? _customLabelValueGap : null,
        ContentAlignmentOverride = _useCustomAlignment ? _customContentAlignment : null,
        PaddingHOverride = _useCustomPadding ? _customPaddingH : null,
        PaddingVOverride = _useCustomPadding ? _customPaddingV : null,
        BackgroundColorHex = _bgColorHex,
        BackgroundOpacity = _bgOpacity,
        BorderEnabled = _borderEnabled,
        BorderColorHex = _borderColorHex,
        BorderOpacity = _borderOpacity,
        BorderThickness = _borderThickness,
        CornerRadius = _cornerRadius
    };

    public void CopyVisualStyleFrom(OverlayLayerViewModel source)
    {
        if (IsBackdropBoxLayer != source.IsBackdropBoxLayer) return;

        var copy = source.ExportStyleConfig();
        copy.CustomLabel = _customLabel; // Giữ nguyên tên riêng của từng lớp
        copy.DisplayMode = _displayMode; // Giữ nguyên chế độ hiển thị của từng lớp
        ApplyStyleConfig(copy);
    }

    private void NotifyDisplayModeProperties()
    {
        OnPropertyChanged(nameof(ShowHeaderPart));
        OnPropertyChanged(nameof(ShowValuePart));
        OnPropertyChanged(nameof(ShowIconPart));
        OnPropertyChanged(nameof(ShowTextLabelPart));
        OnPropertyChanged(nameof(EffectiveInlineGapMargin));
        OnPropertyChanged(nameof(EffectiveStackedGapMargin));
    }

    private void NotifyFontProperties()
    {
        OnPropertyChanged(nameof(EffectiveFontSize));
        OnPropertyChanged(nameof(EffectiveLabelFontSize));
        OnPropertyChanged(nameof(EffectiveIconFontSize));
    }

    private void NotifyAlignmentProperties()
    {
        OnPropertyChanged(nameof(EffectiveLabelValueGap));
        OnPropertyChanged(nameof(EffectiveAlignment));
        OnPropertyChanged(nameof(IsInlineLayout));
        OnPropertyChanged(nameof(IsSpaceBetweenLayout));
        OnPropertyChanged(nameof(IsStackedLayout));
        OnPropertyChanged(nameof(EffectiveInlineGapMargin));
        OnPropertyChanged(nameof(EffectiveStackedGapMargin));
    }

    private void RebuildCachedBrushes()
    {
        string bgHex = _useGlobalStyle ? _owner.DefaultCellBgColorHex : _bgColorHex;
        double bgOp = _useGlobalStyle ? _owner.DefaultCellBgOpacity : _bgOpacity;
        if (bgOp <= 0.1)
        {
            _cachedBgBrush = TransparentBrush;
        }
        else
        {
            var c = MetricItem.TryParseHex(bgHex, out var parsed) ? parsed : Color.FromRgb(10, 12, 16);
            byte a = (byte)Math.Clamp(Math.Round(bgOp / 100.0 * 255.0), 0, 255);
            _cachedBgBrush = Freeze(new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)));
        }

        bool bdEn = _useGlobalStyle ? _owner.DefaultCellBorderEnabled : _borderEnabled;
        string bdHex = _useGlobalStyle ? _owner.DefaultCellBorderColorHex : _borderColorHex;
        double bdOp = _useGlobalStyle ? _owner.DefaultCellBorderOpacity : _borderOpacity;
        if (!bdEn || bdOp <= 0.1)
        {
            _cachedBorderBrush = TransparentBrush;
        }
        else
        {
            var c = MetricItem.TryParseHex(bdHex, out var parsed) ? parsed : Colors.White;
            byte a = (byte)Math.Clamp(Math.Round(bdOp / 100.0 * 255.0), 0, 255);
            _cachedBorderBrush = Freeze(new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B)));
        }

        _cachedLabelBrush = MetricItem.TryParseHex(_labelColorHex, out var lc)
            ? Freeze(new SolidColorBrush(lc))
            : _owner.GlobalLabelBrush;

        _cachedValueBrush = MetricItem.TryParseHex(_valueColorHex, out var vc)
            ? Freeze(new SolidColorBrush(vc))
            : _owner.GlobalValueBrush;
    }

    private static string NormalizeHex(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string s = raw.Trim();
        if (!s.StartsWith('#')) s = "#" + s;
        return s.ToUpperInvariant();
    }

    private static SolidColorBrush CreateSolid(string hex, Color fallback)
    {
        var c = MetricItem.TryParseHex(hex, out var parsed) ? parsed : fallback;
        return Freeze(new SolidColorBrush(c));
    }

    private static SolidColorBrush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
