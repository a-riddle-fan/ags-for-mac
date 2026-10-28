using System;
using System.IO;
using System.Linq;
using AGS.Editor.Build;
using AGS.Editor.Hosting;
using AGS.Native;
using AGS.Types;

namespace AGS.Editor.Core.Tests
{
    /// <summary>Game text processors that change the game: make default language, speech numbering, voice script.</summary>
    [Collection(CurrentDirectoryCollection.Name)]
    public class SpeechToolsTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-speech-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        private GameProject Load()
        {
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            KnownBuildTargets.RegisterAll();
            return GameProject.Load(Path.Combine(_gameDir, "Game.agf"), new ConsoleEditorHost(TextWriter.Null));
        }

        private void AssertBuilds(GameProject project)
        {
            project.Save();
            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            BuildResult result = toolchain.Build(project.GameFilePath);
            Assert.True(result.Succeeded, ReferenceGame.BuildFailure(result));
        }

        private string RoomScript(int number) =>
            File.ReadAllText(Path.Combine(_gameDir, $"room{number}.asc"), project_encoding);

        private static readonly System.Text.Encoding project_encoding = AnsiEncoding.Default;

        [Fact]
        public void MakeDefaultLanguageReplacesGameTextAndRekeysTranslations()
        {
            GameProject project = Load();
            Game game = project.Game;
            Translation english = game.Translations.Single(t => t.FileName.Equals("english.trs", StringComparison.OrdinalIgnoreCase));
            Assert.False(TranslationUpdater.UpdateTranslations(project, new[] { english }).HasErrors);
            Translation made = TranslationUpdater.CreateTranslation(project);
            Assert.False(TranslationUpdater.UpdateTranslations(project, new[] { made }).HasErrors);
            made.LoadData();

            // a script line, an inventory item and a line from a room script get new texts
            const string scriptLine = "Ach nö, will heute keine Türen schleppen.";
            const string itemName = "Lukenöffner";
            string roomLine = made.TranslatedLines.Keys.First(k => made.TranslationSections.TryGetValue(k, out var s) && s.Name == "Room2"
                && File.ReadAllText(Path.Combine(_gameDir, "room2.asc"), project_encoding).Contains("\"" + k + "\""));
            made.TranslatedLines[scriptLine] = "No, no doors today.";
            made.TranslatedLines[itemName] = "Hatch opener";
            made.TranslatedLines[roomLine] = "A translated room line";
            made.Modified = true;
            made.SaveData();
            english.LoadData();
            string englishForScriptLine = (english.TranslatedLines.TryGetValue(scriptLine, out string e) ? e : null);

            CompileMessages messages = TranslationUpdater.ReplaceGameTextWithTranslation(project, made);
            Assert.False(messages.HasErrors, string.Join("\n", messages.Select(m => m.Message)));

            Assert.Contains(game.RootInventoryItemFolder.AllItemsFlat, i => i.Description == "Hatch opener");
            Assert.DoesNotContain(game.RootInventoryItemFolder.AllItemsFlat, i => i.Description == itemName);
            Assert.Contains(ScriptSearch.AllScripts(game), s => s.Text.Contains("No, no doors today."));
            Assert.Contains("\"A translated room line\"", RoomScript(2));
            // other translations are keyed on the new default text, keeping their translation
            english.LoadData();
            Assert.False(english.TranslatedLines.ContainsKey(scriptLine));
            Assert.Equal(englishForScriptLine ?? string.Empty, english.TranslatedLines["No, no doors today."]);
            AssertBuilds(project);
        }

        [Fact]
        public void NumberSpeechLinesAndCreateTheVoiceActingScript()
        {
            GameProject project = Load();
            Game game = project.Game;
            CompileMessages messages = SpeechTools.NumberSpeechLines(project, doNarrator: true, combineIdenticalLines: true, removeNumbering: false, characterID: null);
            Assert.False(messages.HasErrors, string.Join("\n", messages.Select(m => m.Message)));

            Assert.True(File.Exists(Path.Combine(_gameDir, SpeechLinesNumbering.SPEECH_REFERENCE_FILE_NAME)));
            Assert.Matches(@"\.Say\(""&\d+ ", RoomScript(2)); // room scripts are numbered and saved
            Assert.Contains(game.RootScriptFolder.AllItemsFlat, m => System.Text.RegularExpressions.Regex.IsMatch(m.Script.Text, @"Say\(""&\d+ "));

            string voice = Path.Combine(_gameDir, "VoiceSpeech.txt");
            messages = SpeechTools.CreateVoiceActingScript(project, voice);
            Assert.False(messages.HasErrors, string.Join("\n", messages.Select(m => m.Message)));
            string[] lines = File.ReadAllLines(voice);
            Assert.Contains(lines, l => l.StartsWith("*** Lines for character "));
            Assert.Contains(lines, l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^c\w+: &\d+ "));
            AssertBuilds(project);

            // Remove numbering again: the lines are back to plain text.
            messages = SpeechTools.NumberSpeechLines(project, true, true, removeNumbering: true, characterID: null);
            Assert.False(messages.HasErrors, string.Join("\n", messages.Select(m => m.Message)));
            Assert.DoesNotMatch(@"\.Say\(""&\d+ ", RoomScript(2));
            AssertBuilds(project);
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
