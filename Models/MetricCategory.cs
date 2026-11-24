using WinHardwareMonitor.Services;

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
    public static string ToLocalizedName(this MetricCategory category) => category switch
    {
        MetricCategory.Cpu => LocalizationService.Instance["GrpCpu"],
        MetricCategory.Gpu => LocalizationService.Instance["GrpGpuNone"],
        MetricCategory.Ram => LocalizationService.Instance["GrpRam"],
        MetricCategory.Network => LocalizationService.Instance["GrpNetwork"],
        MetricCategory.Storage => LocalizationService.Instance["GrpStorageTotal"],
        MetricCategory.Fps => LocalizationService.Instance["GrpFps"],
        _ => LocalizationService.Instance["GpuVendorOther"]
    };
}
