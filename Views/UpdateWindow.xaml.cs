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

        RefreshLocalizedBadges();
        ReleaseNotesTextBlock.Text = _release.ReleaseNotes;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;

        Closing += (_, _) =>
        {
            LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            _downloadCts = null;
        };
    }

    private void OnLanguageChanged()
    {
        Dispatcher.BeginInvoke(RefreshLocalizedBadges);
    }

    private void RefreshLocalizedBadges()
    {
        var loc = LocalizationService.Instance;
        CurrentVersionBadge.Text = loc.Format("UpdateCurVer", _updateService.CurrentVersionString);
        NewVersionBadge.Text = loc.Format("UpdateNewVer", _release.VersionString);
        LaterButton.Content = _isDownloading ? loc["UpdateBtnCancel"] : loc["UpdateBtnLater"];
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

        var loc = LocalizationService.Instance;
        _isDownloading = true;
        InstallNowButton.IsEnabled = false;
        LaterButton.Content = loc["UpdateBtnCancel"];
        DownloadProgressPanel.Visibility = Visibility.Visible;
        DownloadProgressBar.Value = 0;
        ProgressPercentText.Text = "0%";
        ProgressDetailText.Text = loc.Format("UpdatePreparing", _release.AssetName);

        _downloadCts = new CancellationTokenSource();
        var ct = _downloadCts.Token;

        var progress = new Progress<UpdateDownloadProgress>(p =>
        {
            DownloadProgressBar.Value = p.Percent;
            ProgressPercentText.Text = $"{p.Percent:F0}%";
            if (p.TotalBytes > 0)
            {
                ProgressDetailText.Text = LocalizationService.Instance.Format(
                    "UpdateDownloadingProgress", FormatMb(p.BytesReceived), FormatMb(p.TotalBytes));
            }
            else
            {
                ProgressDetailText.Text = LocalizationService.Instance.Format(
                    "UpdateDownloadingBytes", FormatMb(p.BytesReceived));
            }
        });

        try
        {
            string installerPath = await _updateService.DownloadInstallerAsync(_release, progress, ct);

            ProgressDetailText.Text = LocalizationService.Instance["UpdateLaunching"];
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
                LaterButton.Content = LocalizationService.Instance["UpdateBtnLater"];
                StatusHintText.Text = LocalizationService.Instance["UpdateLaunchFailedShort"];
                MessageBox.Show(
                    LocalizationService.Instance.Format("UpdateLaunchFailed", err ?? string.Empty),
                    LocalizationService.Instance["UpdateWinTitle"],
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException)
        {
            _isDownloading = false;
            InstallNowButton.IsEnabled = true;
            LaterButton.Content = LocalizationService.Instance["UpdateBtnLater"];
            ProgressDetailText.Text = LocalizationService.Instance["UpdateDownloadCancelled"];
        }
        catch (Exception ex)
        {
            _isDownloading = false;
            InstallNowButton.IsEnabled = true;
            LaterButton.Content = LocalizationService.Instance["UpdateBtnLater"];
            ProgressDetailText.Text = LocalizationService.Instance["UpdateDownloadFailedShort"];
            MessageBox.Show(
                LocalizationService.Instance.Format("UpdateDownloadFailed", ex.Message),
                LocalizationService.Instance["UpdateWinErrorTitle"],
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
