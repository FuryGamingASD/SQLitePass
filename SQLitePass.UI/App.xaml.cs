using System;
using System.Threading.Tasks;
using System.Windows;
using SQLitePass.Core.Services;
using SQLitePass.UI.Dialogs;

namespace SQLitePass.UI
{
    public partial class App : System.Windows.Application
    {
        private SQLitePass.UI.Services.SystemTrayCoordinator? _startupTray;
        private bool _openingVault;
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            AppLogger.LogInfo("=== SQLitePass starting ===");

            DispatcherUnhandledException += (s, args) =>
            {
                AppLogger.LogError("DispatcherUnhandledException caught", args.Exception);
                try { if (MainWindow is SQLitePass.UI.MainWindow main) main.LockAfterUnexpectedError(); }
                catch { Shutdown(1); }
                MessageBox.Show(SQLitePass.UI.Services.LocalizationManager.GetString("ErrorUnexpected"), SQLitePass.UI.Services.LocalizationManager.GetString("ErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    AppLogger.LogError("AppDomain.UnhandledException caught", ex);
                }
            };

            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                AppLogger.LogError("TaskScheduler.UnobservedTaskException caught", args.Exception);
                Dispatcher.BeginInvoke(() => { if (MainWindow is SQLitePass.UI.MainWindow main) main.LockAfterUnexpectedError(); });
                args.SetObserved();
            };

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var settings = SQLitePass.Core.Services.AppSettingsManager.Load();
            AppLogger.Configure(settings.EnableDiagnosticLogging);
            SQLitePass.UI.Services.WindowPresentationService.Register();
            SQLitePass.UI.Services.LocalizationManager.ApplyLanguage(settings.Language);
            SQLitePass.UI.Services.ThemeManager.ApplyTheme(settings.DarkMode);

            if (Environment.GetCommandLineArgs().Contains("--minimized", StringComparer.OrdinalIgnoreCase))
            {
                _startupTray = new SQLitePass.UI.Services.SystemTrayCoordinator(
                    OpenVaultWindow,
                    () => new PasswordGeneratorDialog().ShowDialog(),
                    () => { },
                    () => Shutdown());
                return;
            }
            OpenVaultWindow();
        }

        private void OpenVaultWindow()
        {
            if (_openingVault) return;
            _openingVault = true;
            try
            {

            var login = new LoginWindow();
            bool? result = login.ShowDialog();

            if (result == true && login.StorageProvider != null && login.AuthenticatedCrypto != null && login.Metadata != null)
            {
                var mainWin = new MainWindow(login.StorageProvider, login.AuthenticatedCrypto, login.Metadata);
                _startupTray?.Dispose(); _startupTray = null;
                MainWindow = mainWin;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWin.Show();
            }
            else
            {
                if (_startupTray == null) Shutdown();
            }
            }
            finally { _openingVault = false; }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SQLitePass.UI.Services.WindowPresentationService.Flush();
            _startupTray?.Dispose();
            SQLitePass.UI.Services.ClipboardCoordinator.Shared.Dispose();
            base.OnExit(e);
        }
    }
}
