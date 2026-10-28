using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;

namespace AGS.Editor.Avalonia
{
    internal static class Program
    {
        /// <summary>Unhandled exceptions are appended here (~/Library/Logs/AGS Editor/crash.log).</summary>
        public static readonly string CrashLogFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", "AGS Editor", "crash.log");

        /// <summary>Usage: AGSEditor [path/to/Game.agf]</summary>
        [STAThread]
        public static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) => WriteCrashLog("Unhandled exception", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, e) => WriteCrashLog("Unobserved task exception", e.Exception);
            try
            {
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
                WriteCrashLog("Unhandled exception on the UI thread", ex);
                throw;
            }
        }

        public static void WriteCrashLog(string kind, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CrashLogFile));
                File.AppendAllText(CrashLogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {kind}:\n{ex}\n\n");
            }
            catch (Exception) { }
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
        }
    }
}
