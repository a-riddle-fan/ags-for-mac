using System;
using System.IO;
using System.Linq;
using AGS.Editor.Hosting;
using AGS.Types;

namespace AGS.Editor.Core.Tests
{
    /// <summary>Translation update (TranslationGenerator + room texts) against the reference game's .trs files.</summary>
    [Collection(CurrentDirectoryCollection.Name)]
    public class TranslationTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-trs-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            Directory.Delete(_gameDir, true);
        }

        [Fact]
        public void UpdateKeepsTranslationsAndFindsTheGameTexts()
        {
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            GameProject project = GameProject.Load(Path.Combine(_gameDir, "Game.agf"), new ConsoleEditorHost(TextWriter.Null));
            Translation english = project.Game.Translations.Single(t => t.FileName.Equals("english.trs", StringComparison.OrdinalIgnoreCase));
            english.LoadData();
            var before = english.TranslatedLines.ToDictionary(p => p.Key, p => p.Value);

            CompileMessages messages = TranslationUpdater.UpdateTranslations(project, new[] { english });
            Assert.False(messages.HasErrors, string.Join("\n", messages.Select(m => m.Message)));

            english.LoadData();
            var after = english.TranslatedLines;
            // Upstream's merge rules (UpdateSingleTranslationProcess): translated lines are always kept; those no
            // longer in the game are marked OBSOLETE, untranslated ones are dropped.
            foreach (var pair in before.Where(p => !string.IsNullOrEmpty(p.Value)))
                Assert.Equal(pair.Value, after[pair.Key]);
            Assert.All(before.Keys.Except(after.Keys), key => Assert.Equal(string.Empty, before[key]));
            Assert.DoesNotContain("a_button_give", after.Keys); // untranslated entry no longer in the game
            // Texts of the current game, from scripts, rooms, dialogs and items, are found.
            Assert.Contains("Ach nö, will heute keine Türen schleppen.", after.Keys);  // script
            Assert.Contains("Lukenöffner", after.Keys);                               // inventory item
            Assert.Contains(after.Keys, k => english.TranslationSections.TryGetValue(k, out var section) && section.Name.StartsWith("Room"));
        }

        [Fact]
        public void NewTranslationGetsAllTexts()
        {
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            GameProject project = GameProject.Load(Path.Combine(_gameDir, "Game.agf"), new ConsoleEditorHost(TextWriter.Null));
            Translation created = TranslationUpdater.CreateTranslation(project);
            Assert.Equal("New Translation", created.Name);
            Assert.True(File.Exists(Path.Combine(_gameDir, created.FileName)));

            CompileMessages messages = TranslationUpdater.UpdateTranslations(project, new[] { created });
            Assert.False(messages.HasErrors, string.Join("\n", messages.Select(m => m.Message)));
            created.LoadData();
            Translation english = project.Game.Translations.Single(t => t.FileName.Equals("english.trs", StringComparison.OrdinalIgnoreCase));
            english.LoadData();
            Assert.True(created.TranslatedLines.Count >= english.TranslatedLines.Count * 9 / 10,
                $"{created.TranslatedLines.Count} texts vs {english.TranslatedLines.Count} in english.trs");
            Assert.All(created.TranslatedLines.Values, v => Assert.Equal(string.Empty, v));
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
