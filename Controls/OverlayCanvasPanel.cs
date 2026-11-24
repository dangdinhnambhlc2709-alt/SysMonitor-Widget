using System;
using System.Windows;
using System.Windows.Controls;

namespace WinHardwareMonitor.Controls;

/// <summary>
/// Panel Canvas tự co khít cho cửa sổ OSD Overlay thuần túy:
/// - Giữ nguyên 100% vị trí tương đối (X, Y) và kích thước (Width/Height) đã thiết kế trong RTSS-style Editor.
/// - Tự tính khung bao khít (minX..maxX, minY..maxY) của các lớp đang hiển thị để neo sát 9 vị trí màn hình không dư khoảng trống.
/// </summary>
public sealed class OverlayCanvasPanel : Panel
{
    public static readonly DependencyProperty XProperty =
        DependencyProperty.RegisterAttached("X", typeof(double), typeof(OverlayCanvasPanel),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty YProperty =
        DependencyProperty.RegisterAttached("Y", typeof(double), typeof(OverlayCanvasPanel),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsParentMeasure | FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static double GetX(DependencyObject obj) => (double)obj.GetValue(XProperty);
    public static void SetX(DependencyObject obj, double value) => obj.SetValue(XProperty, value);

    public static double GetY(DependencyObject obj) => (double)obj.GetValue(YProperty);
    public static void SetY(DependencyObject obj, double value) => obj.SetValue(YProperty, value);

    private double _minX;
    private double _minY;

    protected override Size MeasureOverride(Size availableSize)
    {
        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        bool anyVisible = false;

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;

            child.Measure(infinite);
            double x = GetX(child);
            double y = GetY(child);
            double w = child.DesiredSize.Width;
            double h = child.DesiredSize.Height;

            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x + w > maxX) maxX = x + w;
            if (y + h > maxY) maxY = y + h;
            anyVisible = true;
        }

        if (!anyVisible)
        {
            _minX = 0;
            _minY = 0;
            return new Size(0, 0);
        }

        _minX = minX;
        _minY = minY;
        return new Size(Math.Ceiling(Math.Max(0, maxX - minX)), Math.Ceiling(Math.Max(0, maxY - minY)));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
                continue;

            double x = GetX(child) - _minX;
            double y = GetY(child) - _minY;
            child.Arrange(new Rect(x, y, child.DesiredSize.Width, child.DesiredSize.Height));
        }
        return finalSize;
    }
}
