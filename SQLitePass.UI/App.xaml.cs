using System.Windows;
using SQLitePass.UI.Dialogs;

namespace SQLitePass.UI
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var settings = SQLitePass.Core.Services.AppSettingsManager.Load();
            SQLitePass.UI.Services.LocalizationManager.ApplyLanguage(settings.Language);
            SQLitePass.UI.Services.ThemeManager.ApplyTheme(settings.DarkMode);

            var login = new LoginWindow();
            bool? result = login.ShowDialog();

            if (result == true && login.StorageProvider != null && login.AuthenticatedCrypto != null && login.Metadata != null)
            {
                var mainWin = new MainWindow(login.StorageProvider, login.AuthenticatedCrypto, login.Metadata);
                MainWindow = mainWin;
                ShutdownMode = ShutdownMode.OnMainWindowClose;
                mainWin.Show();
            }
            else
            {
                Shutdown();
            }
        }
    }
}
