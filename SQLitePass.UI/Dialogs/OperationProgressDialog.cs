using System.Windows;
using System.Windows.Controls;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using SQLitePass.UI.Services;

namespace SQLitePass.UI.Dialogs;

internal sealed class OperationProgressDialog : Window
{
    private readonly CancellationTokenSource _cancellation;
    private readonly TextBlock _status;
    private readonly ProgressBar _progress;
    private readonly Button _cancel;
    private readonly bool _exporting;
    private bool _completed;
    private bool _closed;

    public OperationProgressDialog(CancellationTokenSource cancellation, bool exporting = false)
    {
        _cancellation = cancellation; _exporting = exporting;
        SetResourceReference(TitleProperty, exporting ? "ExportTitle" : "ImportTitle"); Width = 420; SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "AppBg");
        var panel = new StackPanel { Margin = new Thickness(24) };
        _status = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _status.SetResourceReference(TextBlock.TextProperty, exporting ? "ExportWriting" : "ImportReading");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
        System.Windows.Automation.AutomationProperties.SetLiveSetting(_status, System.Windows.Automation.AutomationLiveSetting.Polite);
        _progress = new ProgressBar { Height = 8, IsIndeterminate = true, Margin = new Thickness(0, 16, 0, 16) };
        _cancel = new Button { MinWidth = 100, HorizontalAlignment = HorizontalAlignment.Right };
        _cancel.SetResourceReference(ContentControl.ContentProperty, "Cancel");
        _cancel.Click += (_, _) => Cancel();
        panel.Children.Add(_status); panel.Children.Add(_progress); panel.Children.Add(_cancel); Content = panel;
        Closing += (_, _) => { if (!_completed) _cancellation.Cancel(); };
        Closed += (_, _) => _closed = true;
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Cancel(); e.Handled = true; } };
        WindowSecurityService.ApplyProtection(this, AppSettingsManager.Load().ProtectScreenCapture);
    }

    private void Cancel()
    {
        _cancellation.Cancel(); _cancel.IsEnabled = false; _status.SetResourceReference(TextBlock.TextProperty, _exporting ? "ExportCancelling" : "ImportCancelling");
    }

    public void Report(VaultImportProgress value)
    {
        if (_closed || _cancellation.IsCancellationRequested) return;
        _progress.IsIndeterminate = false; _progress.Maximum = value.Total; _progress.Value = value.Completed;
        _status.Text = LocalizationManager.GetString("ImportProgress", value.Completed, value.Total);
        System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(_status)?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }

    public void ReportExport(int count)
    {
        if (_closed || _cancellation.IsCancellationRequested) return;
        _status.Text = LocalizationManager.GetString("ExportProgress", count);
        System.Windows.Automation.Peers.UIElementAutomationPeer.FromElement(_status)?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }

    public void Complete()
    {
        _completed = true; if (!_closed) Close();
    }
}
