using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinHardwareMonitor.Models;
using WinHardwareMonitor.ViewModels;
using Wpf.Ui.Controls;

namespace WinHardwareMonitor.Views;

public static class MetricDragDrop
{
    public const string DataFormat = "WinHardwareMonitor.MetricDragPayload";
}

public sealed record MetricDragPayload(string MetricId, bool FromActiveList, int SourceIndex);

public partial class SettingsWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private Point _dragStartPoint;
    private MetricItem? _dragCandidate;
    private bool _dragFromActiveList;
    private int _dragSourceIndex = -1;

    private static readonly SolidColorBrush NormalDropBorderBrush = new(Color.FromArgb(0x1F, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush HighlightDropBorderBrush = new(Color.FromArgb(0xCC, 0x60, 0xCD, 0xFF));

    public SettingsWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private void AvailableCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject))
        {
            _dragCandidate = null;
            return;
        }

        if (sender is FrameworkElement fe && fe.DataContext is MetricItem item)
        {
            _dragStartPoint = e.GetPosition(null);
            _dragCandidate = item;
            _dragFromActiveList = false;
            _dragSourceIndex = -1;
        }
    }

    private void AvailableCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate == null || _dragFromActiveList)
            return;

        var diff = e.GetPosition(null) - _dragStartPoint;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var payload = new MetricDragPayload(_dragCandidate.Id, false, -1);
            var data = new DataObject(MetricDragDrop.DataFormat, payload);
            _dragCandidate = null;
            DragDrop.DoDragDrop((DependencyObject)sender, data, DragDropEffects.Copy);
        }
    }

    private void ActiveList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject))
        {
            _dragCandidate = null;
            return;
        }

        var container = FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (container?.DataContext is MetricItem item)
        {
            _dragStartPoint = e.GetPosition(null);
            _dragCandidate = item;
            _dragFromActiveList = true;
            _dragSourceIndex = _viewModel.ActiveMetrics.IndexOf(item);
        }
        else
        {
            _dragCandidate = null;
        }
    }

    private void ActiveList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragCandidate == null || !_dragFromActiveList)
            return;

        var diff = e.GetPosition(null) - _dragStartPoint;
        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            var payload = new MetricDragPayload(_dragCandidate.Id, true, _dragSourceIndex);
            var data = new DataObject(MetricDragDrop.DataFormat, payload);
            _dragCandidate = null;
            DragDrop.DoDragDrop(ActiveMetricsListBox, data, DragDropEffects.Move);
        }
    }

    private void ActiveList_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(MetricDragDrop.DataFormat))
        {
            var payload = e.Data.GetData(MetricDragDrop.DataFormat) as MetricDragPayload;
            e.Effects = payload?.FromActiveList == true ? DragDropEffects.Move : DragDropEffects.Copy;
            ActiveDropZoneBorder.BorderBrush = HighlightDropBorderBrush;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void ActiveList_DragLeave(object sender, DragEventArgs e)
    {
        ActiveDropZoneBorder.BorderBrush = NormalDropBorderBrush;
    }

    private void ActiveList_Drop(object sender, DragEventArgs e)
    {
        ActiveDropZoneBorder.BorderBrush = NormalDropBorderBrush;

        if (!e.Data.GetDataPresent(MetricDragDrop.DataFormat))
            return;

        if (e.Data.GetData(MetricDragDrop.DataFormat) is not MetricDragPayload payload)
            return;

        int targetIndex = CalculateDropIndex(e.GetPosition(ActiveMetricsListBox));

        if (payload.FromActiveList)
        {
            int currentIdx = -1;
            for (int i = 0; i < _viewModel.ActiveMetrics.Count; i++)
            {
                if (_viewModel.ActiveMetrics[i].Id == payload.MetricId)
                {
                    currentIdx = i;
                    break;
                }
            }

            if (currentIdx >= 0)
            {
                int finalIdx = Math.Clamp(targetIndex, 0, Math.Max(0, _viewModel.ActiveMetrics.Count - 1));
                _viewModel.MoveActiveMetric(currentIdx, finalIdx);
            }
        }
        else
        {
            _viewModel.AddMetricAt(payload.MetricId, targetIndex);
        }

        e.Handled = true;
    }

    private int CalculateDropIndex(Point positionInList)
    {
        int count = ActiveMetricsListBox.Items.Count;
        for (int i = 0; i < count; i++)
        {
            if (ActiveMetricsListBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
            {
                var topLeft = item.TranslatePoint(new Point(0, 0), ActiveMetricsListBox);
                double midY = topLeft.Y + item.ActualHeight / 2.0;
                if (positionInList.Y < midY)
                    return i;
            }
        }

        return count;
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        return FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(source) != null;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
