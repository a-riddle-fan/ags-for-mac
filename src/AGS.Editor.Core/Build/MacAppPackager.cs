using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security;
using AGS.Types;

namespace AGS.Editor.Build
{
    /// <summary>
    /// macOS output target (new in ags4mac; upstream builds Windows/Linux packages from engine binaries that
    /// don't exist on macOS): wraps the built game data (Compiled/Data) and the native engine into
    /// Compiled/macOS/&lt;Game&gt;.app. The engine changes into the bundle's Resources folder at start-up
    /// (platform/osx AGSMacInitPaths) and finds the game data there.
    /// </summary>
    public static class MacAppPackager
    {
        public const string OutputFolder = "macOS";

        public static string Package(GameProject project, AgsBuildToolchain toolchain)
        {
            Game game = project.Game;
            string dataDir = Path.Combine(project.Directory, "Compiled", "Data");
            string gameFileName = string.IsNullOrWhiteSpace(game.Settings.GameFileName) ? "game" : game.Settings.GameFileName;
            if (!File.Exists(Path.Combine(dataDir, gameFileName + ".ags")))
                throw new AGSEditorException("Build the game first: Compiled/Data/" + gameFileName + ".ags was not found.");
            string engine = Path.Combine(toolchain.ToolsDirectory, "ags");
            if (!File.Exists(engine))
                throw new AGSEditorException("The AGS engine for macOS was not found next to the build tools.");

            string appName = SafeName(string.IsNullOrWhiteSpace(game.Settings.GameName) ? gameFileName : game.Settings.GameName);
            string outDir = Path.Combine(project.Directory, "Compiled", OutputFolder);
            string app = Path.Combine(outDir, appName + ".app");
            if (Directory.Exists(app)) Directory.Delete(app, true);
            string macOS = Path.Combine(app, "Contents", "MacOS");
            string resources = Path.Combine(app, "Contents", "Resources");
            Directory.CreateDirectory(macOS);
            Directory.CreateDirectory(resources);

            // engine (resolve the symlink of the tools folder)
            string executable = Path.Combine(macOS, gameFileName);
            File.Copy(new FileInfo(engine).ResolveLinkTarget(true)?.FullName ?? engine, executable);
            File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

            // game data: everything the Data target produced (.ags, audio.vox, speech.vox, .tra, acsetup.cfg, …)
            CopyDirectory(dataDir, resources);

            bool hasIcon = MakeIcon(project, resources);
            File.WriteAllText(Path.Combine(app, "Contents", "Info.plist"), InfoPlist(game, gameFileName, appName, hasIcon));
            Sign(app);
            return app;
        }

        private static string SafeName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars().Concat(new[] { ':' }))
                name = name.Replace(c, '_');
            return name.Trim();
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), true);
            foreach (string dir in Directory.GetDirectories(source))
            {
                if (Path.GetFileName(dir).StartsWith("_", StringComparison.Ordinal)) continue; // _temp etc.
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
            }
        }

        // The game's icon (user.ico, as the Windows target uses) as an .icns.
        private static bool MakeIcon(GameProject project, string resources)
        {
            string ico = Path.Combine(project.Directory, "user.ico");
            if (!File.Exists(ico)) return false;
            string iconset = Path.Combine(Path.GetTempPath(), "ags4mac-icon-" + Guid.NewGuid().ToString("N") + ".iconset");
            try
            {
                Directory.CreateDirectory(iconset);
                string png = Path.Combine(iconset, "source.png");
                if (!Run("sips", "-s", "format", "png", ico, "--out", png)) return false;
                foreach (int size in new[] { 16, 32, 128, 256, 512 })
                {
                    Run("sips", "-z", size.ToString(), size.ToString(), png, "--out", Path.Combine(iconset, $"icon_{size}x{size}.png"));
                    Run("sips", "-z", (size * 2).ToString(), (size * 2).ToString(), png, "--out", Path.Combine(iconset, $"icon_{size}x{size}@2x.png"));
                }
                File.Delete(png);
                return Run("iconutil", "-c", "icns", iconset, "-o", Path.Combine(resources, "game.icns"));
            }
            finally
            {
                try { Directory.Delete(iconset, true); } catch (IOException) { }
            }
        }

        private static string InfoPlist(Game game, string executable, string appName, bool hasIcon)
        {
            string id = "com.ags4mac.game." + new string(executable.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            string Esc(string s) => SecurityElement.Escape(s ?? string.Empty);
            return $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
  <key>CFBundleExecutable</key><string>{Esc(executable)}</string>
  <key>CFBundleName</key><string>{Esc(appName)}</string>
  <key>CFBundleDisplayName</key><string>{Esc(game.Settings.GameName)}</string>
  <key>CFBundleIdentifier</key><string>{Esc(id)}</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>{Esc(string.IsNullOrEmpty(game.Settings.Version) ? "1.0" : game.Settings.Version)}</string>
  <key>CFBundleVersion</key><string>1</string>
  {(hasIcon ? "<key>CFBundleIconFile</key><string>game.icns</string>" : "")}
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.adventure-games</string>
</dict>
</plist>
";
        }

        // Ad-hoc signature so that the bundle runs on Apple silicon (distribution needs a Developer ID).
        private static void Sign(string app) => Run("codesign", "--force", "--deep", "-s", "-", app);

        private static bool Run(string tool, params string[] args)
        {
            var psi = new ProcessStartInfo(tool) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string a in args) psi.ArgumentList.Add(a);
            try
            {
                using Process p = Process.Start(psi);
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
        }
    }
}
