using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WinHardwareMonitor.Services;

namespace WinHardwareMonitor.Controls;

/// <summary>
/// Biểu đồ đường FPS siêu nhẹ vẽ trực tiếp qua DrawingContext (OnRender), không phình VisualTree.
/// Tối ưu tái sử dụng Pen & Typeface đóng băng để tiêu thụ CPU/GPU gần như bằng 0.
/// </summary>
public class FpsGraphControl : FrameworkElement
{
    public static readonly DependencyProperty PointsProperty =
        DependencyProperty.Register(nameof(Points), typeof(IReadOnlyList<FpsGraphPoint>), typeof(FpsGraphControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BaseLineBrushProperty =
        DependencyProperty.Register(nameof(BaseLineBrush), typeof(Brush), typeof(FpsGraphControl),
            new FrameworkPropertyMetadata(Freeze(new SolidColorBrush(Color.FromRgb(74, 222, 128))), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FrameGenLineBrushProperty =
        DependencyProperty.Register(nameof(FrameGenLineBrush), typeof(Brush), typeof(FpsGraphControl),
            new FrameworkPropertyMetadata(Freeze(new SolidColorBrush(Color.FromRgb(96, 205, 255))), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GuideBrushProperty =
        DependencyProperty.Register(nameof(GuideBrush), typeof(Brush), typeof(FpsGraphControl),
            new FrameworkPropertyMetadata(Freeze(new SolidColorBrush(Color.FromArgb(42, 255, 255, 255))), FrameworkPropertyMetadataOptions.AffectsRender));

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
    private static readonly Typeface DefaultTypeface = new("Segoe UI");
    private static readonly DashStyle FgDashStyle = FreezeDash(new DashStyle(new[] { 3.0, 2.0 }, 0));

    private Brush? _cachedBaseBrushRef;
    private Pen? _cachedBasePen;
    private LinearGradientBrush? _cachedBaseFill;

    private Brush? _cachedFgBrushRef;
    private Pen? _cachedFgPen;

    private Brush? _cachedGuideBrushRef;
    private Pen? _cachedBorderPen;
    private Pen? _cachedGridPen;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w < 16 || h < 12)
            return;

        EnsureGuidePens();

        dc.DrawRoundedRectangle(PlotBgBrush, _cachedBorderPen, new Rect(0, 0, w, h), 4, 4);
        dc.DrawLine(_cachedGridPen!, new Point(2, h * 0.5), new Point(w - 2, h * 0.5));

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

        double yMax = Math.Ceiling((maxVal * 1.12) / 30.0) * 30.0;
        if (yMax < 60) yMax = 60;

        double padX = 3.0;
        double padY = 3.0;
        double plotW = Math.Max(1, w - padX * 2);
        double plotH = Math.Max(1, h - padY * 2);
        int count = pts.Count;
        double stepX = plotW / Math.Max(1, count - 1);

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

        EnsureBasePenAndFill();
        if (_cachedBaseFill != null)
            dc.DrawGeometry(_cachedBaseFill, null, baseFillGeo);
        dc.DrawGeometry(null, _cachedBasePen!, baseStrokeGeo);

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
            EnsureFgPen();
            dc.DrawGeometry(null, _cachedFgPen!, fgGeo);
        }

        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var maxText = new FormattedText(
            $"{yMax:F0}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            DefaultTypeface,
            9.0,
            ScaleTextBrush,
            dpi);
        dc.DrawText(maxText, new Point(w - maxText.Width - 4, 1));
    }

    private void EnsureGuidePens()
    {
        var g = GuideBrush;
        if (ReferenceEquals(_cachedGuideBrushRef, g) && _cachedBorderPen != null && _cachedGridPen != null)
            return;

        _cachedGuideBrushRef = g;
        _cachedBorderPen = new Pen(g, 0.8);
        _cachedBorderPen.Freeze();
        _cachedGridPen = new Pen(g, 0.6) { DashStyle = DashStyles.Dot };
        _cachedGridPen.Freeze();
    }

    private void EnsureBasePenAndFill()
    {
        var b = BaseLineBrush;
        if (ReferenceEquals(_cachedBaseBrushRef, b) && _cachedBasePen != null)
            return;

        _cachedBaseBrushRef = b;
        _cachedBasePen = new Pen(b, 1.6)
        {
            LineJoin = PenLineJoin.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        _cachedBasePen.Freeze();

        if (b is SolidColorBrush solid)
        {
            var grad = new LinearGradientBrush(
                Color.FromArgb(64, solid.Color.R, solid.Color.G, solid.Color.B),
                Color.FromArgb(4, solid.Color.R, solid.Color.G, solid.Color.B),
                90);
            grad.Freeze();
            _cachedBaseFill = grad;
        }
        else
        {
            _cachedBaseFill = null;
        }
    }

    private void EnsureFgPen()
    {
        var b = FrameGenLineBrush;
        if (ReferenceEquals(_cachedFgBrushRef, b) && _cachedFgPen != null)
            return;

        _cachedFgBrushRef = b;
        _cachedFgPen = new Pen(b, 1.45)
        {
            LineJoin = PenLineJoin.Round,
            DashStyle = FgDashStyle
        };
        _cachedFgPen.Freeze();
    }

    private void DrawIdleHint(DrawingContext dc, double w, double h)
    {
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var idleText = new FormattedText(
            "--",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            DefaultTypeface,
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

    private static DashStyle FreezeDash(DashStyle style)
    {
        style.Freeze();
        return style;
    }
}
