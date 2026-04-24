using System.Configuration;
using System.Data;
using System.Windows;
using Serilog;

namespace Big_A_Stock_Calculator
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Async(a => a.File("logs/app-.log", rollingInterval: RollingInterval.Day))
                .WriteTo.Console()
                .CreateLogger();

            Log.Information("Application Starting...");

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("Application Exiting...");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}
