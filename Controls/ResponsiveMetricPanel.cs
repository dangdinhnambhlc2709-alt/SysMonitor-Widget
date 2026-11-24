using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace WinHardwareMonitor.Controls;

/// <summary>
/// Panel tự động dàn hàng các mục thông số theo chiều ngang hoặc chiều dọc khi người dùng co giãn Widget,
/// bỏ qua các mục đang bị ẩn (Collapsed) để không tạo khoảng trống thừa và đảm bảo không cắt chữ.
/// </summary>
public class ResponsiveMetricPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double), typeof(ResponsiveMetricPanel),
            new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(nameof(VerticalSpacing), typeof(double), typeof(ResponsiveMetricPanel),
            new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    private int _computedColumns = 1;
    private bool _singleRowNatural;

    private List<UIElement> GetVisibleChildren()
    {
        var visible = new List<UIElement>(InternalChildren.Count);
        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility != Visibility.Collapsed)
                visible.Add(child);
        }
        return visible;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visibleChildren = GetVisibleChildren();
        int count = visibleChildren.Count;
        if (count == 0)
            return new Size(0, 0);

        double hGap = HorizontalSpacing;
        double vGap = VerticalSpacing;

        double maxChildWidth = 0;
        double maxChildHeight = 0;
        double totalNaturalWidth = 0;

        var infinite = new Size(double.PositiveInfinity, double.PositiveInfinity);
        foreach (var child in visibleChildren)
        {
            child.Measure(infinite);
            maxChildWidth = Math.Max(maxChildWidth, child.DesiredSize.Width);
            maxChildHeight = Math.Max(maxChildHeight, child.DesiredSize.Height);
            totalNaturalWidth += child.DesiredSize.Width;
        }

        totalNaturalWidth += Math.Max(0, count - 1) * hGap;

        double availWidth = double.IsInfinity(availableSize.Width) || availableSize.Width <= 0
            ? totalNaturalWidth
            : availableSize.Width;

        // Dàn 1 hàng ngang tự nhiên nếu vừa đủ chiều rộng
        if (totalNaturalWidth <= availWidth + 0.5)
        {
            _singleRowNatural = true;
            _computedColumns = count;
            return new Size(totalNaturalWidth, maxChildHeight);
        }

        _singleRowNatural = false;

        double minColWidth = Math.Max(1, maxChildWidth);
        int cols = (int)Math.Floor((availWidth + hGap) / (minColWidth + hGap));
        cols = Math.Clamp(cols, 1, count);
        _computedColumns = cols;

        double cellWidth = Math.Max(1, (availWidth - (cols - 1) * hGap) / cols);

        // Đo lại từng phần tử theo chiều rộng cột thực tế và tính chiều cao từng hàng riêng biệt
        int rows = (int)Math.Ceiling(count / (double)cols);
        double totalHeight = 0;

        for (int r = 0; r < rows; r++)
        {
            double rowH = 0;
            for (int c = 0; c < cols; c++)
            {
                int idx = r * cols + c;
                if (idx >= count) break;
                var child = visibleChildren[idx];
                child.Measure(new Size(cellWidth, double.PositiveInfinity));
                rowH = Math.Max(rowH, child.DesiredSize.Height);
            }
            totalHeight += rowH;
            if (r < rows - 1) totalHeight += vGap;
        }

        return new Size(availWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var visibleChildren = GetVisibleChildren();
        int count = visibleChildren.Count;
        if (count == 0)
            return finalSize;

        double hGap = HorizontalSpacing;
        double vGap = VerticalSpacing;

        if (_singleRowNatural)
        {
            double x = 0;
            double rowHeight = 0;
            foreach (var child in visibleChildren)
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);

            double yOffset = Math.Max(0, (finalSize.Height - rowHeight) / 2.0);
            foreach (var child in visibleChildren)
            {
                double w = child.DesiredSize.Width;
                double cy = yOffset + Math.Max(0, (rowHeight - child.DesiredSize.Height) / 2.0);
                child.Arrange(new Rect(x, cy, w, child.DesiredSize.Height));
                x += w + hGap;
            }
            return finalSize;
        }

        int cols = Math.Clamp(_computedColumns, 1, count);
        double cellWidth = Math.Max(1, (finalSize.Width - (cols - 1) * hGap) / cols);
        int rows = (int)Math.Ceiling(count / (double)cols);

        var rowHeights = new double[rows];
        double totalHeight = 0;
        for (int r = 0; r < rows; r++)
        {
            double rowH = 0;
            for (int c = 0; c < cols; c++)
            {
                int idx = r * cols + c;
                if (idx >= count) break;
                rowH = Math.Max(rowH, visibleChildren[idx].DesiredSize.Height);
            }
            rowHeights[r] = rowH;
            totalHeight += rowH;
            if (r < rows - 1) totalHeight += vGap;
        }

        double y = totalHeight < finalSize.Height ? (finalSize.Height - totalHeight) / 2.0 : 0;
        for (int r = 0; r < rows; r++)
        {
            double rowH = rowHeights[r];
            for (int c = 0; c < cols; c++)
            {
                int idx = r * cols + c;
                if (idx >= count) break;
                var child = visibleChildren[idx];
                double x = c * (cellWidth + hGap);
                double cy = y + Math.Max(0, (rowH - child.DesiredSize.Height) / 2.0);
                child.Arrange(new Rect(x, cy, cellWidth, child.DesiredSize.Height));
            }
            y += rowH + vGap;
        }

        return finalSize;
    }
}
