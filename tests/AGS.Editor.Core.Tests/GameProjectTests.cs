using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using AGS.Editor;
using AGS.Editor.Hosting;
using AGS.Types;

namespace AGS.Editor.Core.Tests
{
    /// <summary>
    /// GameProject loads and saves a copy of the reference game. Not run in parallel with other tests:
    /// like upstream, loading changes the process's current directory.
    /// </summary>
    [Collection(CurrentDirectoryCollection.Name)]
    public class GameProjectTests : IDisposable
    {
        private readonly string _gameDir;
        private readonly StringWriter _messages = new StringWriter();
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public GameProjectTests()
        {
            AnsiEncoding.EnsureCodePagesRegistered();
            _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-project-" + Guid.NewGuid().ToString("N"));
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            ReferenceGame.RegisterBuildTargets();
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            Directory.Delete(_gameDir, true);
        }

        private GameProject Load() => GameProject.Load(Path.Combine(_gameDir, "Game.agf"), new ConsoleEditorHost(_messages));

        [Fact]
        public void LoadsScriptsRelativeToTheGameFolder()
        {
            GameProject project = Load();

            Script global = project.Game.RootScriptFolder.GetScriptByFileName("GlobalScript.asc", true);
            Assert.NotNull(global);
            Assert.True(global.Text.Length > 1000, "global script text was not loaded");
            Assert.Equal(_gameDir, project.Game.DirectoryPath);
        }

        [Fact]
        public void UpgradesOldProjectsLikeUpstream()
        {
            GameProject project = Load();

            // Saved by 3.6.0 (XML index 3060020 < 3060110): SetRestartPoint() is added to game_start.
            Script global = project.Game.RootScriptFolder.GetScriptByFileName("GlobalScript.asc", true);
            string original = File.ReadAllText(Path.Combine(ReferenceGame.Directory, "GlobalScript.asc"), AnsiEncoding.Default);
            Assert.DoesNotContain("SetRestartPoint();", original);
            Assert.Contains("SetRestartPoint();", global.Text);
            Assert.True(project.Game.Fonts.Count >= 3);
        }

        [Fact]
        public void KeepsAllComponentSections()
        {
            GameProject project = Load();

            Assert.Equal(25, project.PreservedComponents.Count);
            Assert.Equal("FileCommands", project.PreservedComponents[0]);
            Assert.Contains("MusicAndSpeech", project.PreservedComponents);
        }

        [Fact]
        public void SaveWritesWindowsCompatibleFileAndIsStable()
        {
            GameProject project = Load();
            project.Save();

            string agf = Path.Combine(_gameDir, "Game.agf");
            string first = File.ReadAllText(agf, AnsiEncoding.Default);
            Assert.StartsWith("<?xml version=\"1.0\" encoding=\"Windows-1252\"?>", first);
            Assert.True(File.Exists(agf + ".bak"), "backup of the previous Game.agf is missing");
            Assert.True(File.Exists(agf + ".user"));

            XDocument doc = XDocument.Parse(first);
            Assert.Equal(25, doc.Root.Elements("Component").Count());
            XElement music = doc.Root.Elements("Component").Single(c => (string)c.Attribute("Name") == "MusicAndSpeech");
            Assert.NotNull(music.Element("PamFiles"));
            Assert.Equal("False", doc.Root.Elements("Component").Single(c => (string)c.Attribute("Name") == "SourceControl").Element("ProjectUnderControl")?.Value);

            // Saving again (after reloading) gives the same file.
            GameProject reloaded = Load();
            reloaded.Save();
            Assert.Equal(first, File.ReadAllText(agf, AnsiEncoding.Default));

            // The script upgrade was saved, and an unchanged script stays byte-identical.
            Assert.Contains("SetRestartPoint();", File.ReadAllText(Path.Combine(_gameDir, "GlobalScript.asc"), AnsiEncoding.Default));
            Assert.Equal(File.ReadAllBytes(Path.Combine(ReferenceGame.Directory, "Doors.asc")), File.ReadAllBytes(Path.Combine(_gameDir, "Doors.asc")));
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

    [CollectionDefinition(Name, DisableParallelization = true)]
    public class CurrentDirectoryCollection
    {
        public const string Name = "Changes current directory";
    }

    internal static class ReferenceGame
    {
        /// <summary>A failed build's exit code, error messages and the end of its log (the start is just setup).</summary>
        public static string BuildFailure(AGS.Editor.Build.BuildResult result)
        {
            string[] lines = (result.Log ?? string.Empty).Split('\n');
            return $"exit code {result.ExitCode}\n" +
                string.Join("\n", System.Linq.Enumerable.Select(result.Messages, m => $"{m.GetType().Name}: {m.ScriptName}({m.LineNumber}) {m.Message}")) +
                "\n--- log (end) ---\n" + string.Join("\n", System.Linq.Enumerable.Skip(lines, System.Math.Max(0, lines.Length - 60)));
        }

        public static string Directory
        {
            get
            {
                string dir = Path.GetDirectoryName(typeof(ReferenceGame).Assembly.Location);
                while (dir != null && !File.Exists(Path.Combine(dir, "AGS4Mac.sln")))
                    dir = Path.GetDirectoryName(dir);
                Assert.NotNull(dir);
                return Path.Combine(dir, "testdata", "Bernard-SP");
            }
        }

        public static void RegisterBuildTargets() => AGS.Editor.Build.KnownBuildTargets.RegisterAll();
    }
}
