using System;
using System.Windows;
using System.Windows.Interop;
using WinHardwareMonitor.Services;
using WinHardwareMonitor.ViewModels;

namespace WinHardwareMonitor.Views;

/// <summary>
/// Cửa sổ Overlay OSD chuyên dụng (thuần túy, 100% xuyên chuột, không chiếm tiêu điểm game).
/// Toàn bộ vị trí, neo màn hình và bố cục bảng lưới được điều khiển từ Cửa sổ Cài đặt.
/// </summary>
public partial class WidgetWindow : Window
{
    private readonly MainViewModel _viewModel;
    private HwndSource? _hwndSource;

    public WidgetWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => RepositionToAnchor();
        SizeChanged += (_, _) => RepositionToAnchor();
        Closed += (_, _) =>
        {
            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }
        };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Win32Helper.ApplyPureOsdWindowStyles(this);

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            _hwndSource = HwndSource.FromHwnd(handle);
            _hwndSource?.AddHook(WndProc);
        }

        RepositionToAnchor();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Helper.WM_NCHITTEST)
        {
            handled = true;
            return Win32Helper.HTTRANSPARENT;
        }

        if (msg == Win32Helper.WM_MOUSEACTIVATE)
        {
            handled = true;
            return Win32Helper.MA_NOACTIVATE;
        }

        return IntPtr.Zero;
    }

    public void RepositionToAnchor()
    {
        Win32Helper.PositionOverlayWindow(
            this,
            _viewModel.SelectedMonitor?.DeviceName,
            _viewModel.OverlayAnchor,
            _viewModel.OverlayOffsetX,
            _viewModel.OverlayOffsetY);
    }
}
