using System.Collections.Generic;

namespace WinHardwareMonitor.Models;

public enum MetricHeaderStyle
{
    Text = 0,
    Icon = 1
}

public enum GpuTechMode
{
    AutoAllGpus = 0,
    SinglePrimaryGpu = 1
}

public enum GameTechOverrideState
{
    Auto = 0,
    ForceOn = 1,
    ForceOff = 2
}

public enum BackgroundImageStretchMode
{
    UniformToFill = 0,
    Uniform = 1,
    Fill = 2
}

public enum WidgetBlurMode
{
    Off = 0,
    SoftGlass = 1,
    FrostedGlass = 2
}

public sealed class MetricStyleConfig
{
    public string? CustomLabel { get; set; }
    public bool IsHighlightedBlock { get; set; }
    public double HighlightIntensity { get; set; } = 60;
    public string? CustomLabelColorHex { get; set; }
    public string? CustomValueColorHex { get; set; }
    public MetricHeaderStyle HeaderStyle { get; set; } = MetricHeaderStyle.Text;
    public string? CustomSymbolName { get; set; }
}

public class AppConfig
{
    public string LanguageCode { get; set; } = "vi";

    public double WidgetLeft { get; set; } = -1;
    public double WidgetTop { get; set; } = -1;
    public double WidgetWidth { get; set; } = 410;
    public double WidgetHeight { get; set; } = 190;

    public List<string> SelectedMetricIds { get; set; } = new();
    public Dictionary<string, MetricStyleConfig> MetricStyles { get; set; } = new();

    public double BackgroundOpacity { get; set; } = 78;
    public string BackgroundColorHex { get; set; } = "#141419";
    public string TextColorHex { get; set; } = "#FFFFFF";

    // Phông chữ, cỡ chữ (10-36) và độ đậm nhạt (7 mức)
    public string FontFamilyName { get; set; } = "Segoe UI";
    public double FontSize { get; set; } = 13;
    public string FontWeightName { get; set; } = "SemiBold";

    // Tự động co giãn nội dung theo kích thước cửa sổ Widget
    public bool AutoScaleContent { get; set; } = true;

    // 3 chế độ hiệu ứng nền Windows 11 (Tắt làm mờ / Kính mềm / Kính mờ)
    public WidgetBlurMode BlurMode { get; set; } = WidgetBlurMode.Off;

    public double CornerRadius { get; set; } = 14;
    public bool IsClickThrough { get; set; } = false;
    public bool IsWidgetTopmost { get; set; } = true;
    public bool IsWidgetVisible { get; set; } = true;

    // Tùy chỉnh ảnh nền PNG cho Widget
    public string? BackgroundImagePath { get; set; }
    public double BackgroundImageOpacity { get; set; } = 85;
    public BackgroundImageStretchMode BackgroundImageStretch { get; set; } = BackgroundImageStretchMode.UniformToFill;

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

    // Công nghệ GPU & Huy hiệu
    public GpuTechMode GpuTechMode { get; set; } = GpuTechMode.AutoAllGpus;
    public string? SelectedPrimaryGpuId { get; set; }
    public bool HideUnsupportedGpuTechs { get; set; } = false;
    public List<string> EnabledGpuTechIds { get; set; } = new();

    public bool UseCustomGpuBadgeColor { get; set; } = false;
    public string CustomGpuBadgeColorHex { get; set; } = "#60CDFF";

    public Dictionary<string, Dictionary<string, GameTechOverrideState>> GameTechProfiles { get; set; } = new();
}
