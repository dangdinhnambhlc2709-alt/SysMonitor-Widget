using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinHardwareMonitor.Models;
using WinHardwareMonitor.Services;
using WinHardwareMonitor.ViewModels;
using Forms = System.Windows.Forms;

namespace WinHardwareMonitor.Views;

public partial class WidgetWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _toastTimer;
    private bool _boundsApplied;

    // Trạng thái kéo thả sắp xếp trực tiếp trên Widget (Ghost Preview + 30% dim + Drop Indicator phát sáng)
    private Point _tilePressPoint;
    private Point _ghostGrabOffset;
    private MetricItem? _dragCandidateItem;
    private FrameworkElement? _dragCandidateElement;
    private bool _isDraggingTile;
    private int _dragSourceIndex = -1;
    private int _pendingDropIndex = -1;
    private Storyboard? _pulseStoryboard;

    public WidgetWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        _pulseStoryboard = TryFindResource("DropIndicatorPulseStoryboard") as Storyboard;

        _toastTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.0)
        };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            StatusToastBorder.Visibility = Visibility.Collapsed;
        };

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Win32Helper.InitializeWidgetDwmSurface(this);
        RefreshDwmBlurEffect();
        Win32Helper.ApplyWidgetWindowStyles(this, _viewModel.IsClickThrough);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_boundsApplied)
            return;
        _boundsApplied = true;

        ApplySavedBoundsOrDefault();
        RefreshDwmBlurEffect();
    }

    public void RefreshDwmBlurEffect()
    {
        Win32Helper.ApplyWidgetBlurMode(
            this,
            _viewModel.BlurMode,
            _viewModel.CornerRadius,
            _viewModel.BackgroundOpacity,
            _viewModel.IsLightBackground);
    }

    private void ApplySavedBoundsOrDefault()
    {
        Width = Math.Clamp(_viewModel.WidgetWidth, MinWidth, 2400);
        Height = Math.Clamp(_viewModel.WidgetHeight, MinHeight, 1600);
        _viewModel.UpdateLiveWidgetSize(Width, Height);

        double savedLeft = _viewModel.WidgetLeft;
        double savedTop = _viewModel.WidgetTop;

        if (savedLeft < -10000 || savedTop < -10000 || (savedLeft == -1 && savedTop == -1))
        {
            PositionTopRightPrimary();
            return;
        }

        var workArea = SystemParameters.WorkArea;
        bool visibleOnAnyScreen = Forms.Screen.AllScreens.Any(s =>
        {
            var b = s.WorkingArea;
            return savedLeft + Width > b.Left - 100 &&
                   savedLeft < b.Right + 100 &&
                   savedTop + Height > b.Top - 100 &&
                   savedTop < b.Bottom + 100;
        });

        if (visibleOnAnyScreen)
        {
            Left = savedLeft;
            Top = savedTop;
        }
        else
        {
            Left = Math.Max(workArea.Left + 24, workArea.Right - Width - 24);
            Top = workArea.Top + 24;
            _viewModel.UpdateWidgetBounds(Left, Top, Width, Height);
        }
    }

    private void PositionTopRightPrimary()
    {
        var workArea = SystemParameters.WorkArea;
        Left = Math.Max(workArea.Left + 24, workArea.Right - Width - 24);
        Top = workArea.Top + 24;
        _viewModel.UpdateWidgetBounds(Left, Top, Width, Height);
    }

    public void CenterOnPrimaryScreen()
    {
        var workArea = SystemParameters.WorkArea;
        Width = Math.Clamp(_viewModel.WidgetWidth, MinWidth, 2400);
        Height = Math.Clamp(_viewModel.WidgetHeight, MinHeight, 1600);
        Left = workArea.Left + (workArea.Width - Width) / 2.0;
        Top = workArea.Top + (workArea.Height - Height) / 2.0;
        _viewModel.UpdateWidgetBounds(Left, Top, Width, Height);

        if (!IsVisible)
        {
            _viewModel.IsWidgetVisible = true;
            Show();
        }
    }

    public void ApplyClickThrough(bool enabled)
    {
        if (enabled && _isDraggingTile)
            CancelVisualTileDrag();

        Win32Helper.ApplyWidgetWindowStyles(this, enabled);
        if (enabled)
        {
            HoverGuideBorder.Visibility = Visibility.Collapsed;
            TopMoveHandle.Visibility = Visibility.Collapsed;
        }
    }

    public void ShowStatusToast(string message)
    {
        StatusToastTextBlock.Text = message;
        StatusToastBorder.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private void RootGrid_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_viewModel.IsClickThrough)
        {
            HoverGuideBorder.Visibility = Visibility.Visible;
            TopMoveHandle.Visibility = Visibility.Visible;
        }
    }

    private void RootGrid_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_isDraggingTile)
        {
            HoverGuideBorder.Visibility = Visibility.Collapsed;
            TopMoveHandle.Visibility = Visibility.Collapsed;
        }
    }

    private void TopMoveHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsClickThrough || e.ChangedButton != MouseButton.Left)
            return;

        e.Handled = true;
        TryDragMoveWindow();
    }

    private void WidgetBody_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsClickThrough || e.ChangedButton != MouseButton.Left || _isDraggingTile)
            return;

        if (_dragCandidateItem == null || Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _dragCandidateItem = null;
            _dragCandidateElement = null;
            TryDragMoveWindow();
        }
    }

    private void TryDragMoveWindow()
    {
        try
        {
            DragMove();
            _viewModel.UpdateWidgetBounds(Left, Top, Width, Height);
        }
        catch { }
    }

    private void Widget_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isDraggingTile)
            CancelVisualTileDrag();
    }

    #region Kéo thả sắp xếp trực tiếp trên Widget (Ghost Preview + 30% Dim + Glowing Drop Indicator)

    private void WidgetMetricTile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.IsClickThrough || e.ChangedButton != MouseButton.Left)
            return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            _dragCandidateItem = null;
            _dragCandidateElement = null;
            TryDragMoveWindow();
            e.Handled = true;
            return;
        }

        if (sender is FrameworkElement fe && fe.DataContext is MetricItem item)
        {
            _tilePressPoint = e.GetPosition(RootGrid);
            _ghostGrabOffset = e.GetPosition(fe);
            _dragCandidateItem = item;
            _dragCandidateElement = fe;
            _dragSourceIndex = _viewModel.ActiveMetrics.IndexOf(item);
        }
    }

    private void RootGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (_viewModel.IsClickThrough)
            return;

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            if (_isDraggingTile)
                CompleteVisualTileDrag();
            _dragCandidateItem = null;
            _dragCandidateElement = null;
            return;
        }

        Point pos = e.GetPosition(RootGrid);

        if (!_isDraggingTile && _dragCandidateItem != null && _dragCandidateElement != null)
        {
            Vector delta = pos - _tilePressPoint;
            if (Math.Abs(delta.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(delta.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                BeginVisualTileDrag(_dragCandidateItem, _dragCandidateElement, pos);
            }
        }

        if (_isDraggingTile)
        {
            UpdateGhostAndDropIndicator(pos);
        }
    }

    private void RootGrid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingTile)
        {
            CompleteVisualTileDrag();
            e.Handled = true;
        }

        _dragCandidateItem = null;
        _dragCandidateElement = null;
    }

    private void RootGrid_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isDraggingTile)
            CancelVisualTileDrag();
    }

    private void BeginVisualTileDrag(MetricItem item, FrameworkElement tileElement, Point currentPos)
    {
        _isDraggingTile = true;
        _pendingDropIndex = _dragSourceIndex;

        // 1. Chụp ảnh nét của mục đang cầm để tạo Ghost Preview bán trong suốt
        try
        {
            var dpi = VisualTreeHelper.GetDpi(tileElement);
            int pxW = Math.Max(1, (int)Math.Ceiling(tileElement.ActualWidth * dpi.DpiScaleX));
            int pxH = Math.Max(1, (int)Math.Ceiling(tileElement.ActualHeight * dpi.DpiScaleY));

            var rtb = new RenderTargetBitmap(pxW, pxH, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            rtb.Render(tileElement);
            rtb.Freeze();

            DragGhostImage.Source = rtb;
            DragGhostImage.Width = tileElement.ActualWidth;
            DragGhostImage.Height = tileElement.ActualHeight;
        }
        catch
        {
            DragGhostImage.Source = null;
        }

        // 2. Làm mờ vị trí gốc xuống 30%
        item.IsBeingDragged = true;

        // 3. Hiển thị Ghost Preview & bắt chuột mượt 60fps
        DragGhostPreview.Visibility = Visibility.Visible;
        RootGrid.CaptureMouse();
        _pulseStoryboard?.Begin(this, isControllable: true);

        UpdateGhostAndDropIndicator(currentPos);
    }

    private void UpdateGhostAndDropIndicator(Point cursorPos)
    {
        // Di chuyển Ghost Preview bám sát con trỏ chuột
        double ghostX = cursorPos.X - _ghostGrabOffset.X;
        double ghostY = cursorPos.Y - _ghostGrabOffset.Y;
        Canvas.SetLeft(DragGhostPreview, ghostX);
        Canvas.SetTop(DragGhostPreview, ghostY);

        // Tìm mục thông số gần con trỏ nhất để hiển thị Drop Indicator phát sáng + khung nét đứt
        int bestIndex = _dragSourceIndex;
        double bestDistSq = double.MaxValue;
        Rect bestRect = Rect.Empty;
        bool insertAfter = false;

        int count = _viewModel.ActiveMetrics.Count;
        for (int i = 0; i < count; i++)
        {
            if (!_viewModel.ActiveMetrics[i].IsVisibleOnWidget)
                continue;

            if (WidgetItemsControl.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container ||
                container.Visibility != Visibility.Visible ||
                container.ActualWidth <= 0)
            {
                continue;
            }

            Point topLeft = container.TranslatePoint(new Point(0, 0), RootGrid);
            var rect = new Rect(topLeft, new Size(container.ActualWidth, container.ActualHeight));

            double cx = rect.Left + rect.Width / 2.0;
            double cy = rect.Top + rect.Height / 2.0;
            double dx = cursorPos.X - cx;
            double dy = cursorPos.Y - cy;
            double distSq = dx * dx + dy * dy;

            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestIndex = i;
                bestRect = rect;
                insertAfter = cursorPos.X > cx;
            }
        }

        if (bestRect.IsEmpty || bestIndex < 0)
        {
            DropIndicatorBar.Visibility = Visibility.Collapsed;
            DropTargetDashRect.Visibility = Visibility.Collapsed;
            return;
        }

        // Tính chỉ số đích chính xác
        int targetIdx = bestIndex;
        if (insertAfter && bestIndex < _dragSourceIndex)
            targetIdx = bestIndex + 1;
        else if (!insertAfter && bestIndex > _dragSourceIndex)
            targetIdx = bestIndex - 1;
        targetIdx = Math.Clamp(targetIdx, 0, Math.Max(0, count - 1));
        _pendingDropIndex = targetIdx;

        // Vẽ khung nét đứt nhấp nháy ôm gọn vị trí đích
        DropTargetDashRect.Width = Math.Max(8, bestRect.Width + 4);
        DropTargetDashRect.Height = Math.Max(8, bestRect.Height + 4);
        Canvas.SetLeft(DropTargetDashRect, bestRect.Left - 2);
        Canvas.SetTop(DropTargetDashRect, bestRect.Top - 2);
        DropTargetDashRect.Visibility = Visibility.Visible;

        // Vẽ vạch phát sáng màu xanh tại khe sắp thả vào
        double barX = insertAfter ? bestRect.Right + 2 : bestRect.Left - 6;
        barX = Math.Clamp(barX, 4, Math.Max(4, RootGrid.ActualWidth - 8));
        DropIndicatorBar.Height = Math.Max(14, bestRect.Height + 4);
        Canvas.SetLeft(DropIndicatorBar, barX);
        Canvas.SetTop(DropIndicatorBar, bestRect.Top - 2);
        DropIndicatorBar.Visibility = Visibility.Visible;
    }

    private void CompleteVisualTileDrag()
    {
        int src = _dragSourceIndex;
        int dst = _pendingDropIndex;

        CleanupDragVisuals();

        if (src >= 0 && dst >= 0 && src != dst && dst < _viewModel.ActiveMetrics.Count)
        {
            _viewModel.MoveActiveMetric(src, dst);
        }
    }

    private void CancelVisualTileDrag()
    {
        CleanupDragVisuals();
    }

    private void CleanupDragVisuals()
    {
        _isDraggingTile = false;

        foreach (var item in _viewModel.ActiveMetrics)
        {
            if (item.IsBeingDragged)
                item.IsBeingDragged = false;
        }

        if (RootGrid.IsMouseCaptured)
            RootGrid.ReleaseMouseCapture();

        _pulseStoryboard?.Stop(this);
        DragGhostPreview.Visibility = Visibility.Collapsed;
        DropIndicatorBar.Visibility = Visibility.Collapsed;
        DropTargetDashRect.Visibility = Visibility.Collapsed;
        DragGhostImage.Source = null;

        _dragCandidateItem = null;
        _dragCandidateElement = null;
        _dragSourceIndex = -1;
        _pendingDropIndex = -1;
    }

    #endregion

    #region Co giãn tự do 8 hướng (4 cạnh + 4 góc) + Tự động co giãn nội dung mượt

    private void ResizeRight_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        _viewModel.UpdateLiveWidgetSize(Width, Height);
    }

    private void ResizeBottom_DragDelta(object sender, DragDeltaEventArgs e)
    {
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
        _viewModel.UpdateLiveWidgetSize(Width, Height);
    }

    private void ResizeLeft_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double newWidth = Math.Max(MinWidth, Width - e.HorizontalChange);
        double delta = Width - newWidth;
        Left += delta;
        Width = newWidth;
        _viewModel.UpdateLiveWidgetSize(Width, Height);
    }

    private void ResizeTop_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double newHeight = Math.Max(MinHeight, Height - e.VerticalChange);
        double delta = Height - newHeight;
        Top += delta;
        Height = newHeight;
        _viewModel.UpdateLiveWidgetSize(Width, Height);
    }

    private void ResizeTopLeft_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeLeft_DragDelta(sender, e);
        ResizeTop_DragDelta(sender, e);
    }

    private void ResizeTopRight_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeRight_DragDelta(sender, e);
        ResizeTop_DragDelta(sender, e);
    }

    private void ResizeBottomLeft_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeLeft_DragDelta(sender, e);
        ResizeBottom_DragDelta(sender, e);
    }

    private void ResizeBottomRight_DragDelta(object sender, DragDeltaEventArgs e)
    {
        ResizeRight_DragDelta(sender, e);
        ResizeBottom_DragDelta(sender, e);
    }

    private void Resize_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _viewModel.UpdateWidgetBounds(Left, Top, Width, Height);
    }

    #endregion

    #region Kéo thả thẻ mới từ Bảng điều khiển vào Widget

    private void Widget_DragOver(object sender, DragEventArgs e)
    {
        if (!_viewModel.IsClickThrough && e.Data.GetDataPresent(MetricDragDrop.DataFormat))
        {
            e.Effects = DragDropEffects.Copy;
            HoverGuideBorder.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void Widget_DragLeave(object sender, DragEventArgs e)
    {
        HoverGuideBorder.Visibility = Visibility.Collapsed;
    }

    private void Widget_Drop(object sender, DragEventArgs e)
    {
        HoverGuideBorder.Visibility = Visibility.Collapsed;

        if (_viewModel.IsClickThrough || !e.Data.GetDataPresent(MetricDragDrop.DataFormat))
            return;

        if (e.Data.GetData(MetricDragDrop.DataFormat) is MetricDragPayload payload && !payload.FromActiveList)
        {
            _viewModel.AddMetricAt(payload.MetricId, _viewModel.ActiveMetrics.Count);
            ShowStatusToast("Đã thêm thông số vào Widget");
            e.Handled = true;
        }
    }

    #endregion
}
