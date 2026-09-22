using System;
using Avalonia;

namespace SecureGateway.Mac
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                App.WriteCrashLog(ex);
                throw;
            }
        }

        public static AppBuilder BuildAvaloniaApp() =>
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
