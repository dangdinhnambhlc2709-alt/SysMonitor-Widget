using System;
using System.Threading;
using System.Windows;
using WinHardwareMonitor.Services;
using Wpf.Ui.Controls;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;

namespace WinHardwareMonitor.Views;

public partial class UpdateWindow : FluentWindow
{
    private readonly UpdateService _updateService;
    private readonly UpdateReleaseInfo _release;
    private readonly Action _beforeLaunchInstaller;
    private CancellationTokenSource? _downloadCts;
    private bool _isDownloading;

    public UpdateWindow(
        UpdateService updateService,
        UpdateReleaseInfo release,
        Action beforeLaunchInstaller)
    {
        _updateService = updateService;
        _release = release;
        _beforeLaunchInstaller = beforeLaunchInstaller;

        InitializeComponent();

        CurrentVersionBadge.Text = $"Hiện tại: v{_updateService.CurrentVersionString}";
        NewVersionBadge.Text = $"Mới nhất: v{_release.VersionString}";
        ReleaseNotesTextBlock.Text = _release.ReleaseNotes;

        Closing += (_, _) =>
        {
            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _downloadCts = null;
        };
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDownloading)
        {
            _downloadCts?.Cancel();
        }
        Close();
    }

    private async void InstallNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDownloading)
            return;

        _isDownloading = true;
        InstallNowButton.IsEnabled = false;
        LaterButton.Content = "Hủy tải";
        DownloadProgressPanel.Visibility = Visibility.Visible;
        DownloadProgressBar.Value = 0;
        ProgressPercentText.Text = "0%";
        ProgressDetailText.Text = $"Đang tải {_release.AssetName}...";

        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;

        var progress = new Progress<UpdateDownloadProgress>(p =>
        {
            DownloadProgressBar.Value = p.Percent;
            ProgressPercentText.Text = $"{p.Percent:F0}%";
            if (p.TotalBytes > 0)
            {
                ProgressDetailText.Text = $"Đang tải... {FormatMb(p.BytesReceived)} / {FormatMb(p.TotalBytes)}";
            }
            else
            {
                ProgressDetailText.Text = $"Đang tải... {FormatMb(p.BytesReceived)}";
            }
        });

        try
        {
            string installerPath = await _updateService.DownloadInstallerAsync(_release, progress, ct);

            ProgressDetailText.Text = "Tải xong! Đang khởi chạy bộ cài đặt cập nhật...";
            ProgressPercentText.Text = "100%";
            DownloadProgressBar.Value = 100;

            // Đóng sạch tiến trình nền PresentMon và giải phóng tài nguyên trước khi chạy Setup.exe
            _beforeLaunchInstaller();

            if (UpdateService.LaunchSilentUpgradeInstaller(installerPath, out string? err))
            {
                Application.Current.Shutdown();
            }
            else
            {
                _isDownloading = false;
                InstallNowButton.IsEnabled = true;
                LaterButton.Content = "Để sau";
                StatusHintText.Text = "Không thể khởi chạy bộ cài đặt.";
                MessageBox.Show(
                    $"Không thể khởi chạy tệp cài đặt cập nhật:\n{err}",
                    "Cập nhật phần mềm",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            _isDownloading = false;
            InstallNowButton.IsEnabled = true;
            LaterButton.Content = "Để sau";
            ProgressDetailText.Text = "Đã hủy quá trình tải xuống.";
        }
        catch (Exception ex)
        {
            _isDownloading = false;
            InstallNowButton.IsEnabled = true;
            LaterButton.Content = "Để sau";
            ProgressDetailText.Text = "Tải xuống thất bại.";
            MessageBox.Show(
                $"Không thể tải bản cập nhật:\n{ex.Message}",
                "Cập nhật phần mềm — Lỗi",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string FormatMb(long bytes)
    {
        double mb = bytes / (1024.0 * 1024.0);
        return $"{mb:F1} MB";
    }
}
