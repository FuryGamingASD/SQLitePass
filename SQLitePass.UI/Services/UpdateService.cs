using System;
using System.Threading.Tasks;
using System.Windows;
using Velopack;
using Velopack.Sources;

namespace SQLitePass.UI.Services
{
    public static class UpdateService
    {
        // GitHub repository URL for auto-updates
        public static string GitHubRepoUrl { get; set; } = "https://github.com/FuryGamingASD/SQLitePass";

        public static async Task CheckForUpdatesAsync(bool silent = false)
        {
            try
            {
                var source = new GithubSource(GitHubRepoUrl, null, false);
                var mgr = new UpdateManager(source);

                if (!mgr.IsInstalled)
                {
                    if (!silent)
                    {
                        MessageBox.Show(
                            "L'applicazione non è attualmente in esecuzione tramite l'installer Velopack (è in modalità debug/sviluppo).\n\n" +
                            "Gli aggiornamenti automatici saranno attivi una volta installato il pacchetto Setup.exe.",
                            "Verifica Aggiornamenti",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
                    }
                    return;
                }

                var updateInfo = await mgr.CheckForUpdatesAsync().ConfigureAwait(true);
                if (updateInfo == null)
                {
                    if (!silent)
                    {
                        MessageBox.Show(
                            $"Sei già all'ultima versione disponibile ({mgr.CurrentVersion}).",
                            "Nessun aggiornamento",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
                    }
                    return;
                }

                var result = MessageBox.Show(
                    $"È disponibile una nuova versione: v{updateInfo.TargetFullRelease.Version}!\n\nVuoi scaricarla e riavviare l'applicazione ora?",
                    "Aggiornamento Disponibile",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result == MessageBoxResult.Yes)
                {
                    await mgr.DownloadUpdatesAsync(updateInfo).ConfigureAwait(true);
                    mgr.ApplyUpdatesAndRestart(updateInfo);
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    MessageBox.Show(
                        $"Impossibile verificare gli aggiornamenti da GitHub:\n{ex.Message}\n\nAssicurati che il repository sia accessibile e che ci siano release pubblicate.",
                        "Errore Aggiornamento",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                }
            }
        }
    }
}
