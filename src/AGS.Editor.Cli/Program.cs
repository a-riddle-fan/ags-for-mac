using System;
using System.Linq;
using System.IO;
using AGS.Editor;
using AGS.Editor.Build;
using AGS.Editor.Hosting;
using AGS.Types;

namespace AGS.Editor.Cli
{
    /// <summary>
    /// ags-build: loads an AGS project with the macOS editor core and builds its game data with
    /// upstream's toolchain. Phase 1 acceptance harness for ags4mac.
    /// </summary>
    internal static class Program
    {
        private const string Usage =
@"usage: ags-build <path/to/Game.agf> [options]

Loads the project like the AGS editor does (including project upgrades) and builds
the game data (.ags, audio.vox, translations, acsetup.cfg) with agsbuild.

options:
  --out <dir>     output folder (default: <project>/Compiled/Data)
  --tools <dir>   folder with agsbuild and the other AGS tools
                  (default: $AGS4MAC_TOOLS, or build/agstools of the ags4mac checkout)
  --resave        save the project first, as the editor does before building
                  (writes Game.agf, Game.agf.bak, Game.agf.user and changed scripts)
  --check         only load the project and report; don't build
  -v, --verbose   show the full agsbuild output";

        private static int Main(string[] args)
        {
            string gameFile = null, outDir = null, toolsDir = null;
            bool resave = false, checkOnly = false, verbose = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--out" when i + 1 < args.Length: outDir = args[++i]; break;
                    case "--tools" when i + 1 < args.Length: toolsDir = args[++i]; break;
                    case "--resave": resave = true; break;
                    case "--check": checkOnly = true; break;
                    case "-v":
                    case "--verbose": verbose = true; break;
                    case "-h":
                    case "--help": Console.WriteLine(Usage); return 0;
                    default:
                        if (args[i].StartsWith("-") || gameFile != null)
                        {
                            Console.Error.WriteLine($"ags-build: unexpected argument '{args[i]}'\n\n{Usage}");
                            return 2;
                        }
                        gameFile = args[i];
                        break;
                }
            }
            if (gameFile == null)
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }
            if (!File.Exists(gameFile))
            {
                Console.Error.WriteLine($"ags-build: '{gameFile}' not found");
                return 2;
            }

            try
            {
                return Run(Path.GetFullPath(gameFile), outDir, toolsDir, resave, checkOnly, verbose);
            }
            catch (AGSEditorException ex)
            {
                Console.Error.WriteLine($"ags-build: {ex.Message}");
                return 1;
            }
        }

        private static int Run(string gameFile, string outDir, string toolsDir, bool resave, bool checkOnly, bool verbose)
        {
            var host = new ConsoleEditorHost(Console.Error);
            KnownBuildTargets.RegisterAll();

            GameProject project = GameProject.Load(gameFile, host);
            Game game = project.Game;
            Console.WriteLine($"Loaded \"{game.Settings.GameName}\" (saved by AGS {game.SavedXmlEditorVersion ?? "?"}): " +
                $"{game.Rooms.Count} rooms, {game.Characters.Count} characters, {game.RootViewFolder.AllItemsFlat.Count()} views, " +
                $"{game.RootSpriteFolder.CountSpritesInAllSubFolders()} sprites, {game.RootScriptFolder.AllItemsFlat.Count()} script modules, " +
                $"text encoding {game.TextEncoding.WebName}");

            if (checkOnly)
                return 0;

            if (resave)
            {
                project.Save();
                Console.WriteLine($"Saved {project.GameFilePath}");
            }

            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(toolsDir, AppContext.BaseDirectory);
            if (toolchain == null)
            {
                Console.Error.WriteLine("ags-build: agsbuild not found; run scripts/build-upstream-tools.sh or pass --tools <dir>.");
                return 1;
            }

            Console.WriteLine($"Building with {toolchain.AgsBuildPath} ...");
            BuildResult result = toolchain.Build(gameFile, outDir, verbose ? Console.WriteLine : null);
            foreach (CompileMessage message in result.Messages)
                Console.Error.WriteLine($"{(message is CompileError ? "error" : "warning")}: {message.Message}");

            if (!result.Succeeded)
            {
                if (!verbose)
                    Console.Error.WriteLine(result.Log);
                Console.Error.WriteLine("Build FAILED.");
                return 1;
            }

            Console.WriteLine($"Build succeeded: {result.OutputDirectory}");
            foreach (string file in Directory.GetFiles(result.OutputDirectory))
                Console.WriteLine($"  {Path.GetFileName(file),-24} {new FileInfo(file).Length,12:N0} bytes");
            return 0;
        }
    }
}
