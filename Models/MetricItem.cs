using System;
using System.Windows;
using System.Windows.Media;
using WinHardwareMonitor.Services;
using WinHardwareMonitor.ViewModels;
using Wpf.Ui.Controls;

namespace WinHardwareMonitor.Models;

/// <summary>
/// Đại diện gọn nhẹ cho một chỉ số phần cứng / FPS trong kho thông số.
/// </summary>
public class MetricItem : ObservableObject
{
    private string _formattedValue = "--";
    private bool _isPlacedOnGrid;
    private bool _isVisibleOnOverlay = true;

    private string _groupTitle = string.Empty;
    private string _groupSubtitle = string.Empty;
    private string _title = string.Empty;
    private string _shortLabel = string.Empty;
    private string _description = string.Empty;

    public string Id { get; init; } = string.Empty;
    public MetricCategory Category { get; init; }
    public string GroupKey { get; init; } = string.Empty;
    public Func<string>? GroupTitleProvider { get; init; }
    public Func<string>? GroupSubtitleProvider { get; init; }
    public Func<string>? TitleProvider { get; init; }
    public Func<string>? ShortLabelProvider { get; init; }
    public Func<string>? DescriptionProvider { get; init; }

    public bool IsGraphMetric { get; init; }
    public bool IsGpuBadgesMetric { get; init; }
    public SymbolRegular DefaultSymbol { get; init; } = SymbolRegular.DeveloperBoard24;

    public string GroupTitle
    {
        get => GroupTitleProvider != null ? GroupTitleProvider() : _groupTitle;
        init => _groupTitle = value;
    }

    public string GroupSubtitle
    {
        get => GroupSubtitleProvider != null ? GroupSubtitleProvider() : _groupSubtitle;
        init => _groupSubtitle = value;
    }

    public string Title
    {
        get => TitleProvider != null ? TitleProvider() : _title;
        init => _title = value;
    }

    public string ShortLabel
    {
        get => ShortLabelProvider != null ? ShortLabelProvider() : _shortLabel;
        init => _shortLabel = value;
    }

    public string Description
    {
        get => DescriptionProvider != null ? DescriptionProvider() : _description;
        init => _description = value;
    }

    public string FormattedValue
    {
        get => _formattedValue;
        set => SetProperty(ref _formattedValue, value);
    }

    public bool IsPlacedOnGrid
    {
        get => _isPlacedOnGrid;
        set => SetProperty(ref _isPlacedOnGrid, value);
    }

    public bool IsVisibleOnOverlay
    {
        get => _isVisibleOnOverlay;
        set => SetProperty(ref _isVisibleOnOverlay, value);
    }

    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(GroupTitle));
        OnPropertyChanged(nameof(GroupSubtitle));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(ShortLabel));
        OnPropertyChanged(nameof(Description));
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
}
