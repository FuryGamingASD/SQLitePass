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
        private LoginWindow? _loginWindow;
        internal SQLitePass.UI.Services.SingleInstanceCoordinator? InstanceCoordinator { get; set; }
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
            InstanceCoordinator?.RegisterActivationHandler(RequestWindowActivation);
            InstanceCoordinator?.RegisterShutdownHandler(RequestApplicationShutdown);

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
            _loginWindow = login;
            bool? result;
            try { result = login.ShowDialog(); }
            finally { _loginWindow = null; }

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

        private void RequestWindowActivation()
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var login = Windows.OfType<LoginWindow>().FirstOrDefault(window => window.IsVisible);
                if (login != null)
                {
                    RestoreAndActivate(login);
                    return;
                }

                if (MainWindow is MainWindow main)
                {
                    main.ShowAndRestoreWindow();
                    return;
                }

                if (!_openingVault) OpenVaultWindow();
            }));
        }

        private void RequestApplicationShutdown()
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (MainWindow is MainWindow main)
                    main.ExitForUpdateOrUninstall();
                else
                    Shutdown();
            }));
        }

        private static void RestoreAndActivate(Window window)
        {
            if (!window.IsVisible) window.Show();
            if (window.WindowState == WindowState.Minimized)
                window.WindowState = WindowState.Normal;
            window.ShowInTaskbar = true;
            window.Activate();
            window.Focus();
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
