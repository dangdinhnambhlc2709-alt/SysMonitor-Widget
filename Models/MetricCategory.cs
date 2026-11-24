namespace WinHardwareMonitor.Models;

public enum MetricCategory
{
    Cpu,
    Gpu,
    Ram,
    Network,
    Storage,
    Fps
}

public static class MetricCategoryExtensions
{
    public static string ToVietnameseName(this MetricCategory category) => category switch
    {
        MetricCategory.Cpu => "CPU",
        MetricCategory.Gpu => "GPU",
        MetricCategory.Ram => "RAM",
        MetricCategory.Network => "Mạng",
        MetricCategory.Storage => "Ổ cứng",
        MetricCategory.Fps => "FPS & Game",
        _ => "Khác"
    };
}
