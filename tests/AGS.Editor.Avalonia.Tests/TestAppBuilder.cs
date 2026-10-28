using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(AGS.Editor.Avalonia.Tests.TestAppBuilder))]
// Every opened project sets the process-wide current directory to its game folder (upstream relies on it, e.g.
// for script files); tests running in parallel would save into each other's folders.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]

namespace AGS.Editor.Avalonia.Tests
{
    public class TestAppBuilder
    {
        // Real Skia rendering (not headless drawing), so frames can be captured as screenshots.
        public static AppBuilder BuildAvaloniaApp()
        {
            // never read or overwrite the user's saved panel layout
            Views.MainWindow.LayoutFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ags4mac-tests-layout-" + System.Guid.NewGuid().ToString("N") + ".json");
            // nor the user's editor settings (the tests run with the defaults)
            Services.EditorSettings.SettingsFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ags4mac-tests-settings-" + System.Guid.NewGuid().ToString("N") + ".json");
            return BuildApp();
        }

        private static AppBuilder BuildApp() => AppBuilder.Configure<App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }
}
