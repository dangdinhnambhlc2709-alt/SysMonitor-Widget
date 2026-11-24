using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WinHardwareMonitor.Services;

namespace WinHardwareMonitor.Controls;

/// <summary>
/// Biểu đồ đường FPS siêu nhẹ vẽ trực tiếp qua DrawingContext (OnRender), không phình VisualTree.
/// Hỗ trợ hiển thị đồng thời đường FPS gốc và đường phụ FPS FrameGen.
/// </summary>
public class FpsGraphControl : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(IReadOnlyList<FpsGraphPoint>), typeof(FpsGraphControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BaseLineBrushProperty =
        DependencyProperty.Register(nameof(BaseLineBrush), typeof(Brush), typeof(FpsGraphControl),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(74, 222, 128)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FrameGenLineBrushProperty =
        DependencyProperty.Register(nameof(FrameGenLineBrush), typeof(Brush), typeof(FpsGraphControl),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(96, 205, 255)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GuideBrushProperty =
        DependencyProperty.Register(nameof(GuideBrush), typeof(Brush), typeof(FpsGraphControl),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(42, 255, 255, 255)), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<FpsGraphPoint>? Points
    {
        get => (IReadOnlyList<FpsGraphPoint>?)GetValue(PointsProperty);
        set => SetValue(PointsProperty, value);
    }

    public Brush BaseLineBrush
    {
        get => (Brush)GetValue(BaseLineBrushProperty);
        set => SetValue(BaseLineBrushProperty, value);
    }

    public Brush FrameGenLineBrush
    {
        get => (Brush)GetValue(FrameGenLineBrushProperty);
        set => SetValue(FrameGenLineBrushProperty, value);
    }

    public Brush GuideBrush
    {
        get => (Brush)GetValue(GuideBrushProperty);
        set => SetValue(GuideBrushProperty, value);
    }

    private static readonly SolidColorBrush PlotBgBrush = Freeze(new SolidColorBrush(Color.FromArgb(28, 0, 0, 0)));
    private static readonly SolidColorBrush ScaleTextBrush = Freeze(new SolidColorBrush(Color.FromArgb(165, 255, 255, 255)));

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 16 || h < 12)
            return;

        // Nền biểu đồ bo nhẹ
        dc.DrawRoundedRectangle(PlotBgBrush, new Pen(GuideBrush, 0.8), new Rect(0, 0, w, h), 5, 5);

        // Đường lưới ngang mờ giữa đồ thị
        var gridPen = new Pen(GuideBrush, 0.6) { DashStyle = DashStyles.Dot };
        dc.DrawLine(gridPen, new Point(2, h * 0.5), new Point(w - 2, h * 0.5));

        var pts = Points;
        if (pts == null || pts.Count < 2)
        {
            DrawIdleHint(dc, w, h);
            return;
        }

        double maxVal = 60.0;
        for (int i = 0; i < pts.Count; i++)
        {
            if (pts[i].BaseFps > maxVal) maxVal = pts[i].BaseFps;
            if (pts[i].FrameGenFps is double fg && fg > maxVal) maxVal = fg;
        }

        // Làm tròn trần trục Y cho dễ nhìn (bội số của 30)
        double yMax = Math.Ceiling((maxVal * 1.12) / 30.0) * 30.0;
        if (yMax < 60) yMax = 60;

        double padX = 3.0;
        double padY = 3.0;
        double plotW = Math.Max(1, w - padX * 2);
        double plotH = Math.Max(1, h - padY * 2);
        int count = pts.Count;
        double stepX = plotW / Math.Max(1, count - 1);

        // Vẽ vùng tô chuyển sắc nhẹ + đường FPS gốc
        var baseStrokeGeo = new StreamGeometry();
        var baseFillGeo = new StreamGeometry();

        using (var sCtx = baseStrokeGeo.Open())
        using (var fCtx = baseFillGeo.Open())
        {
            for (int i = 0; i < count; i++)
            {
                double x = padX + i * stepX;
                double y = padY + plotH * (1.0 - Math.Clamp(pts[i].BaseFps / yMax, 0, 1));
                var pt = new Point(x, y);

                if (i == 0)
                {
                    sCtx.BeginFigure(pt, isFilled: false, isClosed: false);
                    fCtx.BeginFigure(new Point(x, padY + plotH), isFilled: true, isClosed: true);
                    fCtx.LineTo(pt, isStroked: false, isSmoothJoin: true);
                }
                else
                {
                    sCtx.LineTo(pt, isStroked: true, isSmoothJoin: true);
                    fCtx.LineTo(pt, isStroked: false, isSmoothJoin: true);
                }
            }

            fCtx.LineTo(new Point(padX + (count - 1) * stepX, padY + plotH), isStroked: false, isSmoothJoin: false);
        }

        baseStrokeGeo.Freeze();
        baseFillGeo.Freeze();

        if (BaseLineBrush is SolidColorBrush baseSolid)
        {
            var grad = new LinearGradientBrush(
                Color.FromArgb(72, baseSolid.Color.R, baseSolid.Color.G, baseSolid.Color.B),
                Color.FromArgb(4, baseSolid.Color.R, baseSolid.Color.G, baseSolid.Color.B),
                90);
            grad.Freeze();
            dc.DrawGeometry(grad, null, baseFillGeo);
        }

        var basePen = new Pen(BaseLineBrush, 1.7)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        basePen.Freeze();
        dc.DrawGeometry(null, basePen, baseStrokeGeo);

        // Vẽ đường phụ FPS FrameGen nếu có dữ liệu
        bool hasAnyFg = false;
        var fgGeo = new StreamGeometry();
        using (var fgCtx = fgGeo.Open())
        {
            bool figureOpen = false;
            for (int i = 0; i < count; i++)
            {
                if (pts[i].FrameGenFps is not double fgVal)
                {
                    figureOpen = false;
                    continue;
                }

                hasAnyFg = true;
                double x = padX + i * stepX;
                double y = padY + plotH * (1.0 - Math.Clamp(fgVal / yMax, 0, 1));
                var pt = new Point(x, y);

                if (!figureOpen)
                {
                    fgCtx.BeginFigure(pt, isFilled: false, isClosed: false);
                    figureOpen = true;
                }
                else
                {
                    fgCtx.LineTo(pt, isStroked: true, isSmoothJoin: true);
                }
            }
        }

        if (hasAnyFg)
        {
            fgGeo.Freeze();
            var fgPen = new Pen(FrameGenLineBrush, 1.5)
            {
                LineJoin = PenLineJoin.Round,
                DashStyle = new DashStyle(new[] { 3.0, 2.0 }, 0)
            };
            fgPen.Freeze();
            dc.DrawGeometry(null, fgPen, fgGeo);
        }

        // Nhãn mốc trần trục Y nhỏ góc trên phải
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var maxText = new FormattedText(
            $"{yMax:F0}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            9.0,
            ScaleTextBrush,
            dpi);
        dc.DrawText(maxText, new Point(w - maxText.Width - 4, 1));
    }

    private void DrawIdleHint(DrawingContext dc, double w, double h)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var idleText = new FormattedText(
            "--",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            10.0,
            ScaleTextBrush,
            dpi);
        dc.DrawText(idleText, new Point((w - idleText.Width) / 2.0, (h - idleText.Height) / 2.0));
    }

    private static SolidColorBrush Freeze(SolidColorBrush brush)
    {
        brush.Freeze();
        return brush;
    }
}
