using System;
using SQLitePass.Core.Services;
using Velopack;

namespace SQLitePass.UI
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Initialize Velopack: handles install, uninstall, update hooks before UI starts
            VelopackApp.Build()
                .OnBeforeUpdateFastCallback(_ => Services.SingleInstanceCoordinator.ShutdownForUpdateOrUninstall())
                .OnBeforeUninstallFastCallback(_ =>
                {
                    Services.SingleInstanceCoordinator.ShutdownForUpdateOrUninstall();
                    AppSettingsManager.RemoveStartupRegistrationForUninstall();
                })
                .Run();

            using var singleInstance = Services.SingleInstanceCoordinator.StartOrSignalExisting(out bool shouldStart);
            if (!shouldStart) return;

            var app = new App { InstanceCoordinator = singleInstance };
            app.InitializeComponent();
            app.Run();
        }
    }
}
