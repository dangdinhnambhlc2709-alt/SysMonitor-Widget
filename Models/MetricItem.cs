using System;
using System.Windows;
using System.Windows.Media;
using WinHardwareMonitor.ViewModels;
using Wpf.Ui.Controls;

namespace WinHardwareMonitor.Models;

public class MetricItem : ObservableObject
{
    private static readonly SolidColorBrush TransparentBrush = Freeze(new SolidColorBrush(Colors.Transparent));

    private string _formattedValue = "--";
    private bool _isSelected;
    private bool _isVisibleOnWidget = true;
    private bool _isBeingDragged;

    // Cấu hình riêng cho từng mục
    private string _customLabel = string.Empty;
    private bool _isHighlightedBlock;
    private double _highlightIntensity = 60;
    private string _customLabelColorHex = string.Empty;
    private string _customValueColorHex = string.Empty;
    private MetricHeaderStyle _headerStyle = MetricHeaderStyle.Text;
    private string _customSymbolName = string.Empty;

    // Màu kế thừa từ giao diện chung của Widget
    private SolidColorBrush _inheritedLabelBrush = Freeze(new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)));
    private SolidColorBrush _inheritedValueBrush = Freeze(new SolidColorBrush(Colors.White));
    private bool _isLightWidgetBackground;
    private double _contentScale = 1.0;

    public Action? OnStyleChanged { get; set; }

    public string Id { get; init; } = string.Empty;
    public MetricCategory Category { get; init; }
    public string GroupKey { get; init; } = string.Empty;
    public string GroupTitle { get; init; } = string.Empty;
    public string GroupSubtitle { get; init; } = string.Empty;
    public bool IsGraphMetric { get; init; }
    public bool IsGpuBadgesMetric { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ShortLabel { get; init; } = string.Empty;
    public SymbolRegular DefaultSymbol { get; init; } = SymbolRegular.DeveloperBoard24;
    public string Description { get; init; } = string.Empty;

    public string FormattedValue
    {
        get => _formattedValue;
        set => SetProperty(ref _formattedValue, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsVisibleOnWidget
    {
        get => _isVisibleOnWidget;
        set => SetProperty(ref _isVisibleOnWidget, value);
    }

    /// <summary>
    /// Làm mờ vị trí gốc xuống 30% trong khi đang kéo mục này trực tiếp trên Widget.
    /// </summary>
    public bool IsBeingDragged
    {
        get => _isBeingDragged;
        set
        {
            if (SetProperty(ref _isBeingDragged, value))
                OnPropertyChanged(nameof(TileOpacity));
        }
    }

    public double TileOpacity => _isBeingDragged ? 0.30 : 1.0;

    public string CustomLabel
    {
        get => _customLabel;
        set
        {
            string normalized = value ?? string.Empty;
            if (SetProperty(ref _customLabel, normalized))
            {
                OnPropertyChanged(nameof(DisplayLabel));
                OnPropertyChanged(nameof(HasCustomizations));
                OnStyleChanged?.Invoke();
            }
        }
    }

    public string DisplayLabel => string.IsNullOrWhiteSpace(_customLabel) ? ShortLabel : _customLabel.Trim();

    public bool IsHighlightedBlock
    {
        get => _isHighlightedBlock;
        set
        {
            if (SetProperty(ref _isHighlightedBlock, value))
            {
                NotifyBlockVisuals();
                OnPropertyChanged(nameof(HasCustomizations));
                OnStyleChanged?.Invoke();
            }
        }
    }

    /// <summary>
    /// Mức độ nổi khối (0% = phẳng tối giản -> 100% = nổi khối 3D rõ nét).
    /// </summary>
    public double HighlightIntensity
    {
        get => _highlightIntensity;
        set
        {
            double clamped = Math.Clamp(Math.Round(value), 0, 100);
            if (SetProperty(ref _highlightIntensity, clamped))
            {
                OnPropertyChanged(nameof(HighlightIntensityLabel));
                NotifyBlockVisuals();
                OnPropertyChanged(nameof(HasCustomizations));
                OnStyleChanged?.Invoke();
            }
        }
    }

    public string HighlightIntensityLabel => $"{HighlightIntensity:F0}%";

    public string CustomLabelColorHex
    {
        get => _customLabelColorHex;
        set
        {
            string normalized = NormalizeHexInput(value);
            if (SetProperty(ref _customLabelColorHex, normalized))
            {
                OnPropertyChanged(nameof(EffectiveLabelBrush));
                OnPropertyChanged(nameof(HasCustomLabelColor));
                OnPropertyChanged(nameof(HasCustomizations));
                OnStyleChanged?.Invoke();
            }
        }
    }

    public string CustomValueColorHex
    {
        get => _customValueColorHex;
        set
        {
            string normalized = NormalizeHexInput(value);
            if (SetProperty(ref _customValueColorHex, normalized))
            {
                OnPropertyChanged(nameof(EffectiveValueBrush));
                OnPropertyChanged(nameof(HasCustomValueColor));
                OnPropertyChanged(nameof(HasCustomizations));
                OnStyleChanged?.Invoke();
            }
        }
    }

    public MetricHeaderStyle HeaderStyle
    {
        get => _headerStyle;
        set
        {
            if (SetProperty(ref _headerStyle, value))
            {
                OnPropertyChanged(nameof(IsTextHeader));
                OnPropertyChanged(nameof(IsIconHeader));
                OnPropertyChanged(nameof(HeaderStyleLabel));
                OnPropertyChanged(nameof(HasCustomizations));
                OnStyleChanged?.Invoke();
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

    public string HeaderStyleLabel => IsIconHeader ? "Biểu tượng" : "Chữ";

    public string CustomSymbolName
    {
        get => _customSymbolName;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (SetProperty(ref _customSymbolName, normalized))
            {
                OnPropertyChanged(nameof(EffectiveSymbol));
                OnPropertyChanged(nameof(HasCustomizations));
                OnStyleChanged?.Invoke();
            }
        }
    }

    public SymbolRegular EffectiveSymbol
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_customSymbolName) &&
                Enum.TryParse<SymbolRegular>(_customSymbolName, true, out var parsed))
            {
                return parsed;
            }
            return DefaultSymbol;
        }
    }

    public bool HasCustomLabelColor => TryParseHex(_customLabelColorHex, out _);
    public bool HasCustomValueColor => TryParseHex(_customValueColorHex, out _);

    public bool HasCustomizations =>
        !string.IsNullOrWhiteSpace(_customLabel) ||
        _isHighlightedBlock ||
        Math.Abs(_highlightIntensity - 60) > 0.5 ||
        HasCustomLabelColor ||
        HasCustomValueColor ||
        _headerStyle != MetricHeaderStyle.Text ||
        !string.IsNullOrWhiteSpace(_customSymbolName);

    public SolidColorBrush EffectiveLabelBrush =>
        TryParseHex(_customLabelColorHex, out var c)
            ? Freeze(new SolidColorBrush(c))
            : _inheritedLabelBrush;

    public SolidColorBrush EffectiveValueBrush =>
        TryParseHex(_customValueColorHex, out var c)
            ? Freeze(new SolidColorBrush(c))
            : _inheritedValueBrush;

    /// <summary>
    /// Nền thẻ con chuyển sắc nhẹ theo Mức độ nổi khối (0% -> 100%).
    /// </summary>
    public Brush BlockBackgroundBrush
    {
        get
        {
            if (!_isHighlightedBlock)
                return TransparentBrush;

            double t = _highlightIntensity / 100.0;
            if (_isLightWidgetBackground)
            {
                byte topA = (byte)Math.Clamp(Math.Round(30 + t * 145), 18, 210);
                byte botA = (byte)Math.Clamp(Math.Round(16 + t * 90), 10, 140);
                var grad = new LinearGradientBrush(
                    Color.FromArgb(topA, 255, 255, 255),
                    Color.FromArgb(botA, 226, 232, 240),
                    90);
                grad.Freeze();
                return grad;
            }
            else
            {
                byte topA = (byte)Math.Clamp(Math.Round(14 + t * 56), 10, 82);
                byte botA = (byte)Math.Clamp(Math.Round(8 + t * 28), 6, 46);
                var grad = new LinearGradientBrush(
                    Color.FromArgb(topA, 255, 255, 255),
                    Color.FromArgb(botA, 180, 195, 230),
                    90);
                grad.Freeze();
                return grad;
            }
        }
    }

    /// <summary>
    /// Viền sáng phía trên thẻ (Top Specular Rim) tăng dần theo Mức độ nổi khối.
    /// </summary>
    public Brush BlockBorderBrush
    {
        get
        {
            if (!_isHighlightedBlock)
                return TransparentBrush;

            double t = _highlightIntensity / 100.0;
            if (_isLightWidgetBackground)
            {
                byte topA = (byte)Math.Clamp(Math.Round(40 + t * 165), 25, 220);
                byte botA = (byte)Math.Clamp(Math.Round(18 + t * 62), 12, 95);
                var grad = new LinearGradientBrush(
                    Color.FromArgb(topA, 255, 255, 255),
                    Color.FromArgb(botA, 15, 23, 42),
                    90);
                grad.Freeze();
                return grad;
            }
            else
            {
                byte topA = (byte)Math.Clamp(Math.Round(24 + t * 142), 18, 185);
                byte botA = (byte)Math.Clamp(Math.Round(8 + t * 34), 6, 52);
                var grad = new LinearGradientBrush(
                    Color.FromArgb(topA, 255, 255, 255),
                    Color.FromArgb(botA, 255, 255, 255),
                    90);
                grad.Freeze();
                return grad;
            }
        }
    }

    public Thickness BlockBorderThickness => _isHighlightedBlock ? new Thickness(1) : new Thickness(0);

    public Thickness BlockPadding
    {
        get
        {
            double s = _contentScale;
            return _isHighlightedBlock
                ? new Thickness(Math.Round(8 * s), Math.Round(4 * s), Math.Round(8 * s), Math.Round(4 * s))
                : new Thickness(Math.Round(2 * s), Math.Round(1 * s), Math.Round(2 * s), Math.Round(1 * s));
        }
    }

    public CornerRadius BlockCornerRadius => new(Math.Round(7 * _contentScale, 1));

    // Độ sâu, độ lan và độ đậm bóng đổ 3D dưới thẻ con
    public double BlockShadowDepth => _isHighlightedBlock ? Math.Round((0.6 + (_highlightIntensity / 100.0) * 5.4) * _contentScale, 2) : 0;
    public double BlockShadowBlur => _isHighlightedBlock ? Math.Round((2.0 + (_highlightIntensity / 100.0) * 18.0) * _contentScale, 2) : 0;
    public double BlockShadowOpacity => _isHighlightedBlock ? Math.Round(0.08 + (_highlightIntensity / 100.0) * 0.56, 3) : 0;

    public void RefreshInheritedTheme(SolidColorBrush globalLabelBrush, SolidColorBrush globalValueBrush, bool isLightBackground, double contentScale)
    {
        _inheritedLabelBrush = globalLabelBrush;
        _inheritedValueBrush = globalValueBrush;
        _isLightWidgetBackground = isLightBackground;
        _contentScale = Math.Clamp(contentScale, 0.72, 2.5);

        OnPropertyChanged(nameof(EffectiveLabelBrush));
        OnPropertyChanged(nameof(EffectiveValueBrush));
        NotifyBlockVisuals();
    }

    public void ApplyStyleConfig(MetricStyleConfig? style)
    {
        _customLabel = style?.CustomLabel ?? string.Empty;
        _isHighlightedBlock = style?.IsHighlightedBlock ?? false;
        _highlightIntensity = Math.Clamp(style?.HighlightIntensity ?? 60, 0, 100);
        _customLabelColorHex = style?.CustomLabelColorHex ?? string.Empty;
        _customValueColorHex = style?.CustomValueColorHex ?? string.Empty;
        _headerStyle = style?.HeaderStyle ?? MetricHeaderStyle.Text;
        _customSymbolName = style?.CustomSymbolName ?? string.Empty;

        OnPropertyChanged(nameof(CustomLabel));
        OnPropertyChanged(nameof(DisplayLabel));
        OnPropertyChanged(nameof(IsHighlightedBlock));
        OnPropertyChanged(nameof(HighlightIntensity));
        OnPropertyChanged(nameof(HighlightIntensityLabel));
        OnPropertyChanged(nameof(CustomLabelColorHex));
        OnPropertyChanged(nameof(CustomValueColorHex));
        OnPropertyChanged(nameof(HasCustomLabelColor));
        OnPropertyChanged(nameof(HasCustomValueColor));
        OnPropertyChanged(nameof(EffectiveLabelBrush));
        OnPropertyChanged(nameof(EffectiveValueBrush));
        OnPropertyChanged(nameof(HeaderStyle));
        OnPropertyChanged(nameof(IsTextHeader));
        OnPropertyChanged(nameof(IsIconHeader));
        OnPropertyChanged(nameof(HeaderStyleLabel));
        OnPropertyChanged(nameof(CustomSymbolName));
        OnPropertyChanged(nameof(EffectiveSymbol));
        OnPropertyChanged(nameof(HasCustomizations));
        NotifyBlockVisuals();
    }

    public MetricStyleConfig? ExportStyleConfig()
    {
        if (!HasCustomizations)
            return null;

        return new MetricStyleConfig
        {
            CustomLabel = string.IsNullOrWhiteSpace(_customLabel) ? null : _customLabel.Trim(),
            IsHighlightedBlock = _isHighlightedBlock,
            HighlightIntensity = _highlightIntensity,
            CustomLabelColorHex = HasCustomLabelColor ? _customLabelColorHex : null,
            CustomValueColorHex = HasCustomValueColor ? _customValueColorHex : null,
            HeaderStyle = _headerStyle,
            CustomSymbolName = string.IsNullOrWhiteSpace(_customSymbolName) ? null : _customSymbolName
        };
    }

    public void ResetCustomizations()
    {
        ApplyStyleConfig(null);
        OnStyleChanged?.Invoke();
    }

    private void NotifyBlockVisuals()
    {
        OnPropertyChanged(nameof(BlockBackgroundBrush));
        OnPropertyChanged(nameof(BlockBorderBrush));
        OnPropertyChanged(nameof(BlockBorderThickness));
        OnPropertyChanged(nameof(BlockPadding));
        OnPropertyChanged(nameof(BlockCornerRadius));
        OnPropertyChanged(nameof(BlockShadowDepth));
        OnPropertyChanged(nameof(BlockShadowBlur));
        OnPropertyChanged(nameof(BlockShadowOpacity));
    }

    private static string NormalizeHexInput(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;
        string s = raw.Trim();
        if (!s.StartsWith('#'))
            s = "#" + s;
        return s.ToUpperInvariant();
    }

    public static bool TryParseHex(string? hex, out Color color)
    {
        color = Colors.White;
        if (string.IsNullOrWhiteSpace(hex))
            return false;

        try
        {
            string s = hex.Trim();
            if (!s.StartsWith('#'))
                s = "#" + s;
            if (s.Length is not (7 or 9))
                return false;

            if (ColorConverter.ConvertFromString(s) is Color parsed)
            {
                color = parsed;
                return true;
            }
        }
        catch { return false; }
        return false;
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
