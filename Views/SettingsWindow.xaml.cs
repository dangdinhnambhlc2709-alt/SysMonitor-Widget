using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using WinHardwareMonitor.Models;
using WinHardwareMonitor.ViewModels;
using Wpf.Ui.Controls;
using TextBox = System.Windows.Controls.TextBox;

namespace WinHardwareMonitor.Views;

public partial class SettingsWindow : FluentWindow
{
    private const string VaultDragFormat = "WinHardwareMonitor.CanvasMetricId";

    private readonly MainViewModel _viewModel;

    // Kéo thả từ Kho thông số sang Canvas
    private Point _vaultDragStart;
    private MetricItem? _vaultDragMetric;

    // Kéo trực tiếp một lớp (Layer) trên Canvas
    private bool _isDraggingLayer;
    private OverlayLayerViewModel? _draggingLayer;
    private Point _layerDragMouseStart;
    private double _layerOriginX;
    private double _layerOriginY;

    // Co giãn kích thước lớp bằng Thumb (không trôi số do cộng dồn delta)
    private double _resizeBaseWidth;
    private double _resizeBaseHeight;
    private double _resizeAccumX;
    private double _resizeAccumY;

    public SettingsWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        _viewModel.EditorCanvasGridChanged += UpdateCanvasGridBrush;
        Loaded += (_, _) => UpdateCanvasGridBrush();
    }

    /// <summary>
    /// Vẽ lưới mịn RTSS bằng DrawingBrush đóng băng (0% CPU, sắc nét từng pixel).
    /// Đường phụ theo GridSnapSize, đường chính nhấn nhẹ mỗi 4 ô lưới.
    /// </summary>
    private void UpdateCanvasGridBrush()
    {
        if (CanvasGridOverlay == null) return;

        if (!_viewModel.ShowEditorGrid)
        {
            CanvasGridOverlay.Background = Brushes.Transparent;
            return;
        }

        double step = Math.Max(4, _viewModel.GridSnapSize);
        double majorStep = step * 4;

        var group = new DrawingGroup();

        var minorPen = new Pen(new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)), 0.75);
        minorPen.Freeze();
        var majorPen = new Pen(new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)), 1.0);
        majorPen.Freeze();

        var minorGeo = new GeometryGroup();
        for (int i = 1; i < 4; i++)
        {
            double pos = i * step;
            minorGeo.Children.Add(new LineGeometry(new Point(pos, 0), new Point(pos, majorStep)));
            minorGeo.Children.Add(new LineGeometry(new Point(0, pos), new Point(majorStep, pos)));
        }
        minorGeo.Freeze();
        group.Children.Add(new GeometryDrawing(null, minorPen, minorGeo));

        var majorGeo = new GeometryGroup();
        majorGeo.Children.Add(new LineGeometry(new Point(0, 0), new Point(0, majorStep)));
        majorGeo.Children.Add(new LineGeometry(new Point(0, 0), new Point(majorStep, 0)));
        majorGeo.Freeze();
        group.Children.Add(new GeometryDrawing(null, majorPen, majorGeo));

        group.Freeze();

        var brush = new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, majorStep, majorStep),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, majorStep, majorStep),
            ViewboxUnits = BrushMappingMode.Absolute
        };
        brush.Freeze();

        CanvasGridOverlay.Background = brush;
    }

    // --- Kéo thông số từ Kho thông số (cột trái) thả thẳng lên Canvas ---

    private void VaultCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideControl<ButtonBase>(e.OriginalSource as DependencyObject))
        {
            _vaultDragMetric = null;
            return;
        }

        if (sender is FrameworkElement fe && fe.DataContext is MetricItem item)
        {
            _vaultDragStart = e.GetPosition(null);
            _vaultDragMetric = item;
        }
    }

    private void VaultCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _vaultDragMetric == null)
            return;

        var diff = e.GetPosition(null) - _vaultDragStart;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var data = new DataObject(VaultDragFormat, _vaultDragMetric.Id);
            _vaultDragMetric = null;
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
        }
    }

    private void CanvasSurface_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(VaultDragFormat) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void CanvasSurface_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(VaultDragFormat)) return;
        if (e.Data.GetData(VaultDragFormat) is not string metricId || string.IsNullOrWhiteSpace(metricId))
            return;

        Point dropPos = e.GetPosition(CanvasSurface);
        _viewModel.AddMetricLayerAt(metricId, Math.Max(0, dropPos.X - 24), Math.Max(0, dropPos.Y - 12));
        CanvasSurface.Focus();
        e.Handled = true;
    }

    // --- Bấm vùng trống trên Canvas để bỏ chọn ---

    private void CanvasSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, CanvasSurface))
        {
            _viewModel.SelectLayer(null);
            CanvasSurface.Focus();
        }
    }

    // --- Chọn và kéo thả di chuyển trực tiếp một lớp (Layer) trên Canvas ---

    private void LayerItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideControl<Thumb>(e.OriginalSource as DependencyObject))
            return;

        if (sender is not FrameworkElement fe || fe.DataContext is not OverlayLayerViewModel layer)
            return;

        _viewModel.SelectLayer(layer);
        _draggingLayer = layer;
        _isDraggingLayer = true;
        _layerDragMouseStart = e.GetPosition(CanvasSurface);
        _layerOriginX = layer.X;
        _layerOriginY = layer.Y;

        fe.CaptureMouse();
        CanvasSurface.Focus();
        e.Handled = true;
    }

    private void LayerItem_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingLayer || _draggingLayer == null || e.LeftButton != MouseButtonState.Pressed)
            return;

        Point current = e.GetPosition(CanvasSurface);
        double dx = current.X - _layerDragMouseStart.X;
        double dy = current.Y - _layerDragMouseStart.Y;

        bool bypassSnap = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        double targetX = _viewModel.SnapCoordinate(Math.Clamp(_layerOriginX + dx, 0, CanvasSurface.Width - 20), bypassSnap);
        double targetY = _viewModel.SnapCoordinate(Math.Clamp(_layerOriginY + dy, 0, CanvasSurface.Height - 16), bypassSnap);

        _draggingLayer.X = targetX;
        _draggingLayer.Y = targetY;
    }

    private void LayerItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingLayer) return;

        _isDraggingLayer = false;
        _draggingLayer = null;
        if (sender is FrameworkElement fe && fe.IsMouseCaptured)
            fe.ReleaseMouseCapture();
        e.Handled = true;
    }

    // --- Co giãn kích thước lớp trực tiếp bằng 3 tay nắm (Phải / Dưới / Góc) ---

    private void ResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is not Thumb thumb || thumb.DataContext is not OverlayLayerViewModel layer)
            return;

        _viewModel.SelectLayer(layer);
        _resizeAccumX = 0;
        _resizeAccumY = 0;

        var parentGrid = FindAncestor<Grid>(thumb);
        var visualBorder = parentGrid?.FindName("LayerVisualBorder") as FrameworkElement;

        _resizeBaseWidth = layer.Width > 0 ? layer.Width : Math.Max(24, Math.Round(visualBorder?.ActualWidth ?? 80));
        _resizeBaseHeight = layer.Height > 0 ? layer.Height : Math.Max(18, Math.Round(visualBorder?.ActualHeight ?? 28));
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb || thumb.DataContext is not OverlayLayerViewModel layer)
            return;

        string dir = thumb.Tag as string ?? string.Empty;
        bool bypassSnap = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        _resizeAccumX += e.HorizontalChange;
        _resizeAccumY += e.VerticalChange;

        if (dir.Contains('E'))
        {
            double nextW = _viewModel.SnapCoordinate(Math.Max(20, _resizeBaseWidth + _resizeAccumX), bypassSnap);
            layer.Width = Math.Clamp(nextW, 20, CanvasSurface.Width);
        }

        if (dir.Contains('S'))
        {
            double nextH = _viewModel.SnapCoordinate(Math.Max(16, _resizeBaseHeight + _resizeAccumY), bypassSnap);
            layer.Height = Math.Clamp(nextH, 16, CanvasSurface.Height);
        }
    }

    // --- Tinh chỉnh nhanh bằng bàn phím (Mũi tên để di chuyển, Shift+Mũi tên 1px, Delete để xóa) ---

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (_viewModel.SelectedTabIndex != 0 || _viewModel.SelectedLayer == null)
            return;

        if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase)
            return;

        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)
            ? 1
            : (_viewModel.SnapToGrid ? _viewModel.GridSnapSize : 2);

        switch (e.Key)
        {
            case Key.Left:
                _viewModel.NudgeSelectedLayer(-step, 0);
                e.Handled = true;
                break;
            case Key.Right:
                _viewModel.NudgeSelectedLayer(step, 0);
                e.Handled = true;
                break;
            case Key.Up:
                _viewModel.NudgeSelectedLayer(0, -step);
                e.Handled = true;
                break;
            case Key.Down:
                _viewModel.NudgeSelectedLayer(0, step);
                e.Handled = true;
                break;
            case Key.Delete:
                _viewModel.RemoveLayer(_viewModel.SelectedLayer);
                e.Handled = true;
                break;
        }
    }

    private void NumericTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            BindingOperations.GetBindingExpression(tb, TextBox.TextProperty)?.UpdateSource();
            CanvasSurface.Focus();
            e.Handled = true;
        }
    }

    private static bool IsInsideControl<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source != null)
        {
            if (source is T) return true;
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private static T? FindAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source != null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }
}
