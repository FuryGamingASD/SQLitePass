using System;
using Velopack;

namespace SQLitePass.UI
{
    public static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Initialize Velopack: handles install, uninstall, update hooks before UI starts
            VelopackApp.Build().Run();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
