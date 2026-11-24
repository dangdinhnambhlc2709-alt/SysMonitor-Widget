using System.Collections.Generic;

namespace WinHardwareMonitor.Models;

public enum MetricHeaderStyle
{
    Text = 0,
    Icon = 1
}

public enum GameTechOverrideState
{
    Auto = 0,
    ForceOn = 1,
    ForceOff = 2
}

public enum OverlayAnchor
{
    TopLeft = 0,
    TopCenter = 1,
    TopRight = 2,
    MiddleLeft = 3,
    MiddleCenter = 4,
    MiddleRight = 5,
    BottomLeft = 6,
    BottomCenter = 7,
    BottomRight = 8
}

public enum CellContentAlignment
{
    InlineWithGap = 0,
    SpaceBetween = 1,
    StackedVertical = 2
}

public enum OverlayLayerKind
{
    Metric = 0,
    StaticText = 1,
    BackdropBox = 2
}

public enum LayerDisplayMode
{
    LabelAndValue = 0,
    ValueOnly = 1,
    LabelOnly = 2
}

public sealed class CellStyleConfig
{
    public bool UseGlobalStyle { get; set; } = true;
    public string? CustomLabel { get; set; }
    public LayerDisplayMode DisplayMode { get; set; } = LayerDisplayMode.LabelAndValue;
    public MetricHeaderStyle HeaderStyle { get; set; } = MetricHeaderStyle.Text;
    public string? CustomSymbolName { get; set; }
    public string? LabelColorHex { get; set; }
    public string? ValueColorHex { get; set; }
    public double? FontSizeOverride { get; set; }
    public double? LabelValueGapOverride { get; set; }
    public CellContentAlignment? ContentAlignmentOverride { get; set; }
    public double? PaddingHOverride { get; set; }
    public double? PaddingVOverride { get; set; }

    // Kiểu dáng ô phẳng riêng (áp dụng khi UseGlobalStyle = false)
    public string? BackgroundColorHex { get; set; }
    public double? BackgroundOpacity { get; set; }
    public bool? BorderEnabled { get; set; }
    public string? BorderColorHex { get; set; }
    public double? BorderOpacity { get; set; }
    public double? BorderThickness { get; set; }
    public double? CornerRadius { get; set; }
}

public sealed class OverlayLayerConfig
{
    public string LayerId { get; set; } = string.Empty;
    public OverlayLayerKind Kind { get; set; } = OverlayLayerKind.Metric;
    public string? MetricId { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }   // 0 = tự co theo nội dung
    public double Height { get; set; }  // 0 = tự co theo nội dung
    public int ZIndex { get; set; }
    public CellStyleConfig? Style { get; set; }
}

/// <summary>
/// Chỉ giữ lại tối thiểu để chuyển đổi an toàn cấu hình cũ (v1.0.x / v2 grid -> v3 Canvas).
/// </summary>
public sealed class LegacyGridPlacementConfig
{
    public string MetricId { get; set; } = string.Empty;
    public int Row { get; set; }
    public int Column { get; set; }
    public int ColumnSpan { get; set; } = 1;
    public CellStyleConfig? Style { get; set; }
}

public class AppConfig
{
    public int ConfigVersion { get; set; } = 3;
    public string LanguageCode { get; set; } = "vi";

    // Vị trí Overlay OSD thuần túy (9 điểm neo + căn lề X/Y + đa màn hình)
    public OverlayAnchor OverlayAnchor { get; set; } = OverlayAnchor.TopLeft;
    public double OverlayOffsetX { get; set; } = 16;
    public double OverlayOffsetY { get; set; } = 16;
    public string? MonitorDeviceName { get; set; }
    public bool IsWidgetTopmost { get; set; } = true;
    public bool IsWidgetVisible { get; set; } = true;
    public bool EnableToggleOverlayHotkey { get; set; } = true;

    // Thiết lập Canvas kiểu RTSS Overlay Editor
    public bool ShowEditorGrid { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public int GridSnapSize { get; set; } = 8;
    public double CellPaddingH { get; set; } = 8;
    public double CellPaddingV { get; set; } = 4;
    public double GlobalLabelValueGap { get; set; } = 8;
    public CellContentAlignment GlobalContentAlignment { get; set; } = CellContentAlignment.InlineWithGap;
    public List<OverlayLayerConfig> OverlayLayers { get; set; } = new();

    // Phông chữ & Kiểu dáng ô phẳng mặc định chung
    public string FontFamilyName { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 13;
    public string FontWeightName { get; set; } = "SemiBold";
    public string TextColorHex { get; set; } = "#FFFFFF";
    public string LabelColorHex { get; set; } = "#60CDFF";

    public string DefaultCellBgColorHex { get; set; } = "#0A0C10";
    public double DefaultCellBgOpacity { get; set; } = 78;
    public bool DefaultCellBorderEnabled { get; set; } = true;
    public string DefaultCellBorderColorHex { get; set; } = "#FFFFFF";
    public double DefaultCellBorderOpacity { get; set; } = 22;
    public double DefaultCellBorderThickness { get; set; } = 1.0;
    public double DefaultCellCornerRadius { get; set; } = 0;

    // Nền khung tổng bọc ngoài bảng Overlay
    public bool ShowOuterFrame { get; set; } = false;
    public string OuterFrameBgColorHex { get; set; } = "#080A0E";
    public double OuterFrameBgOpacity { get; set; } = 80;
    public bool OuterFrameBorderEnabled { get; set; } = true;
    public string OuterFrameBorderColorHex { get; set; } = "#FFFFFF";
    public double OuterFrameBorderOpacity { get; set; } = 25;
    public double OuterFrameBorderThickness { get; set; } = 1.0;
    public double OuterFrameCornerRadius { get; set; } = 6;
    public double OuterFramePadding { get; set; } = 6;

    // FPS & Đồ thị
    public bool AutoHideFpsWhenIdle { get; set; } = false;
    public bool AutoHideFrameGenWhenInactive { get; set; } = false;
    public double FpsGraphHeight { get; set; } = 46;
    public string FpsGraphBaseColorHex { get; set; } = "#4ADE80";
    public string FpsGraphFrameGenColorHex { get; set; } = "#60CDFF";

    // Hệ thống, Cập nhật & System Tray
    public bool RunAtWindowsStartup { get; set; } = false;
    public bool MinimizeToTrayOnClose { get; set; } = true;
    public bool AutoCheckUpdatesOnStartup { get; set; } = true;

    // Công nghệ GPU & Huy hiệu (mở khóa tất cả hãng GPU có mặt trên máy)
    public bool HideUnsupportedGpuTechs { get; set; } = false;
    public List<string> EnabledGpuTechIds { get; set; } = new();
    public bool UseCustomGpuBadgeColor { get; set; } = false;
    public string CustomGpuBadgeColorHex { get; set; } = "#60CDFF";
    public Dictionary<string, Dictionary<string, GameTechOverrideState>> GameTechProfiles { get; set; } = new();

    // Trường tương thích ngược chỉ dùng lúc chuyển đổi tự động cấu hình cũ
    public List<string>? SelectedMetricIds { get; set; }
    public List<LegacyGridPlacementConfig>? GridPlacements { get; set; }
}
