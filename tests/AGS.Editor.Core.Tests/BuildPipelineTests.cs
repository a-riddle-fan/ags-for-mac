using System;
using System.IO;
using AGS.Editor.Build;
using AGS.Editor.Hosting;
using AGS.Types;

namespace AGS.Editor.Core.Tests
{
    /// <summary>
    /// End to end: load the reference game with Editor.Core, save it, build it with upstream's agsbuild.
    /// Needs the tools from scripts/build-upstream-tools.sh (build/agstools).
    /// </summary>
    [Collection(CurrentDirectoryCollection.Name)]
    public class BuildPipelineTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-build-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        [Fact]
        public void LoadSaveAndBuildReferenceGame()
        {
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            KnownBuildTargets.RegisterAll();
            string agf = Path.Combine(_gameDir, "Game.agf");

            GameProject project = GameProject.Load(agf, new ConsoleEditorHost(TextWriter.Null));
            project.Save();

            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            Assert.True(toolchain != null, "agsbuild not found; run scripts/build-upstream-tools.sh");
            BuildResult result = toolchain.Build(agf);

            Assert.True(result.Succeeded, ReferenceGame.BuildFailure(result));
            Assert.Equal(Path.Combine(_gameDir, "Compiled", "Data"), result.OutputDirectory);
            Assert.True(new FileInfo(Path.Combine(result.OutputDirectory, "Bernard-SP.ags")).Length > 1_000_000);
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "audio.vox")));
            Assert.True(File.Exists(Path.Combine(result.OutputDirectory, "english.tra")));
            // The saved project kept its build targets.
            Assert.Equal("Windows, Linux", project.Game.Settings.BuildTargets);
        }

        [Fact]
        public void PackagesTheBuiltGameAsAMacApp()
        {
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            KnownBuildTargets.RegisterAll();
            string agf = Path.Combine(_gameDir, "Game.agf");
            GameProject project = GameProject.Load(agf, new ConsoleEditorHost(TextWriter.Null));
            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            { var built = toolchain.Build(agf); Assert.True(built.Succeeded, ReferenceGame.BuildFailure(built)); }

            string app = MacAppPackager.Package(project, toolchain);
            Assert.EndsWith(Path.Combine("Compiled", "macOS", "Bernard-Starterpack.app"), app);
            string exe = Path.Combine(app, "Contents", "MacOS", "Bernard-SP");
            Assert.True(File.Exists(exe));
            Assert.True(File.Exists(Path.Combine(app, "Contents", "Resources", "Bernard-SP.ags")));
            Assert.True(File.Exists(Path.Combine(app, "Contents", "Resources", "audio.vox")));
            Assert.Contains("<string>Bernard-SP</string>", File.ReadAllText(Path.Combine(app, "Contents", "Info.plist")));

            // The bundled engine finds the game in its Resources folder (prints the info and exits, no window).
            var psi = new System.Diagnostics.ProcessStartInfo(exe, "--tell-data") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = System.Diagnostics.Process.Start(psi);
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            Assert.True(p.WaitForExit(30000));
            Assert.Contains("Bernard-Starterpack", output);
            Assert.Contains(Path.Combine("Contents", "Resources"), output);
        }

        [Fact]
        public void DebuggerBreaksAtABreakpointStepsAndStops()
        {
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            KnownBuildTargets.RegisterAll();
            string agf = Path.Combine(_gameDir, "Game.agf");
            GameProject project = GameProject.Load(agf, new ConsoleEditorHost(TextWriter.Null));
            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            BuildResult build = toolchain.Build(agf);
            Assert.True(build.Succeeded, ReferenceGame.BuildFailure(build));

            // No window or sound during the test: SDL's dummy drivers.
            Environment.SetEnvironmentVariable("SDL_VIDEODRIVER", "dummy");
            Environment.SetEnvironmentVariable("SDL_AUDIODRIVER", "dummy");
            using var debugger = new EngineDebugger(Path.Combine(toolchain.ToolsDirectory, "ags"),
                Path.Combine(_gameDir, "Compiled", "Data", "Bernard-SP.ags"));
            var breaks = new System.Collections.Concurrent.BlockingCollection<DebugCallStack>();
            var exited = new System.Threading.ManualResetEventSlim();
            var log = new System.Text.StringBuilder();
            debugger.BreakAtLocation += stack => breaks.Add(stack);
            debugger.Exited += () => exited.Set();
            debugger.Log += line => { lock (log) log.AppendLine(line); };
            try
            {
                // game_start: "int i = 0;" (1621) and "AdjustLanguage();" (1624)
                debugger.Start(new[] { ("GlobalScript.asc", 1624) });

                Assert.True(breaks.TryTake(out DebugCallStack hit, 60000), "no break; engine output:\n" + log);
                Assert.Equal(DebugSessionState.Paused, debugger.State);
                Assert.Equal(("GlobalScript.asc", 1624), (hit.Lines[0].ScriptName, hit.Lines[0].LineNumber));
                Assert.Null(hit.ErrorMessage);

                // Step into AdjustLanguage(): the next stop is inside it, with game_start below on the stack.
                debugger.StepInto();
                Assert.True(breaks.TryTake(out DebugCallStack step, 30000), "no step break; engine output:\n" + log);
                Assert.True(step.Lines.Count >= 2);
                Assert.Equal(1624, step.Lines[1].LineNumber);

                debugger.Stop();
                Assert.True(exited.Wait(15000), "the engine did not exit");
                Assert.Equal(DebugSessionState.NotRunning, debugger.State);
            }
            finally
            {
                Environment.SetEnvironmentVariable("SDL_VIDEODRIVER", null);
                Environment.SetEnvironmentVariable("SDL_AUDIODRIVER", null);
            }
        }

        [Fact]
        public void ParsesEngineCallStacks()
        {
            DebugCallStack stack = EngineDebugger.ParseCallStack(
                "in \"room1.asc\", line 17\nfrom \"GlobalScript.asc\", line 1624\n(and more...)\n", "Error message");
            Assert.Equal(2, stack.Lines.Count);
            Assert.Equal(("room1.asc", 17), (stack.Lines[0].ScriptName, stack.Lines[0].LineNumber));
            Assert.Equal("Error message", stack.ErrorMessage);
        }

        private static void CopyDirectory(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (string dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
        }
    }
}
