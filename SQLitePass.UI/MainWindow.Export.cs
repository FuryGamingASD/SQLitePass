using System.IO;
using System.Windows;
using Microsoft.Win32;
using SQLitePass.Core.Models;
using SQLitePass.Core.Services;
using SQLitePass.UI.Dialogs;
using SQLitePass.UI.Services;

namespace SQLitePass.UI;

public partial class MainWindow
{
    private CancellationTokenSource? _exportCancellation;

    private async void MenuExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_isLocked || _isLocking || _exportCancellation != null) return;
        int generation = _sessionGeneration;
        if (MessageBox.Show(this, LocalizationManager.GetString("ExportWarning"), LocalizationManager.GetString("ExportTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes ||
            _isLocked || generation != _sessionGeneration) return;
        var file = new SaveFileDialog { Title = LocalizationManager.GetString("ExportTitle"), Filter = "CSV (*.csv)|*.csv", DefaultExt = ".csv", AddExtension = true, FileName = "SQLitePass_Export.csv" };
        if (file.ShowDialog(this) != true || _isLocked || generation != _sessionGeneration) return;
        using var cancellation = new CancellationTokenSource(); _exportCancellation = cancellation;
        OperationProgressDialog? progressWindow = null;
        var storage = _storage; var crypto = _crypto;
        try
        {
            string destination = Path.GetFullPath(file.FileName), vault = Path.GetFullPath(_storage.FilePath);
            if (!Path.GetExtension(destination).Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
                destination.Equals(vault, StringComparison.OrdinalIgnoreCase) || destination.StartsWith(vault + ".", StringComparison.OrdinalIgnoreCase) ||
                destination.Equals(vault + "-wal", StringComparison.OrdinalIgnoreCase) || destination.Equals(vault + "-shm", StringComparison.OrdinalIgnoreCase))
                throw new UserInputException("ExportChooseCsv");
            progressWindow = new OperationProgressDialog(cancellation, exporting: true) { Owner = this }; progressWindow.Show();
            var progress = new Progress<int>(count =>
            {
                if (!_isLocked && generation == _sessionGeneration && !cancellation.IsCancellationRequested) progressWindow.ReportExport(count);
            });
            await storage.ExportCsvAsync(destination, crypto, cancellation.Token, progress);
            if (!_isLocked && generation == _sessionGeneration)
                MessageBox.Show(this, LocalizationManager.GetString("ExportCompleted"), LocalizationManager.GetString("ExportTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            if (!_isLocked && generation == _sessionGeneration) TxtOperationStatus.Text = LocalizationManager.GetString("ErrorCancelled");
        }
        catch (Exception exception)
        {
            if (!_isLocked && generation == _sessionGeneration)
                MessageBox.Show(this, UiErrorMessages.GetText(exception), LocalizationManager.GetString("OperationFailed"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            progressWindow?.Complete();
            if (ReferenceEquals(_exportCancellation, cancellation)) _exportCancellation = null;
        }
    }
}
