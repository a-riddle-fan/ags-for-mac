using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using AGS.Types;

namespace AGS.Editor.Core.Tests
{
    /// <summary>
    /// Loads the reference game (Maniac Mansion Mania "Bernard Starterpack", saved by AGS 3.6.0.53)
    /// through the ported AGS.Types model, following AGSEditor.LoadGameFromXml/SaveGameFilesProcess.
    /// </summary>
    public class GameXmlTests
    {
        // Mirrors AGS.Editor.AGSEditor constants (moving to AGS.Editor.Core in step 1.6).
        private const string XmlRootNodeName = "AGSEditorDocument";
        private const string LatestXmlVersion = "3.0.3.2";
        private const int LatestXmlVersionIndex = 3060316;

        static GameXmlTests()
        {
            // Game.agf is Windows-1252; .NET (Core) only ships UTF-* encodings by default.
            AnsiEncoding.EnsureCodePagesRegistered();

            // The editor registers its build targets at startup; Settings.BuildTargets drops names
            // that aren't registered and available.
            ReferenceGame.RegisterBuildTargets();
        }

        private static string ReferenceGameFile()
        {
            string dir = Path.GetDirectoryName(typeof(GameXmlTests).Assembly.Location);
            while (dir != null && !File.Exists(Path.Combine(dir, "AGS4Mac.sln")))
                dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            string agf = Path.Combine(dir, "testdata", "Bernard-SP", "Game.agf");
            Assert.True(File.Exists(agf), $"Reference game not found: {agf}");
            return agf;
        }

        private static Game LoadGame(XmlDocument doc)
        {
            Assert.Equal(XmlRootNodeName, doc.DocumentElement.Name);
            var game = new Game();
            game.SavedXmlVersion = doc.DocumentElement.Attributes["Version"].InnerText;
            game.SavedXmlVersionIndex = Convert.ToInt32(doc.DocumentElement.Attributes["VersionIndex"].InnerText);
            game.SavedXmlEditorVersion = doc.DocumentElement.Attributes["EditorVersion"]?.InnerText;
            if (doc.FirstChild is XmlDeclaration dec && !string.IsNullOrEmpty(dec.Encoding))
                game.SavedXmlEncodingCodePage = Encoding.GetEncoding(dec.Encoding).CodePage;
            game.FromXml(doc.DocumentElement);
            return game;
        }

        private static string SaveGame(Game game)
        {
            var sw = new StringWriter();
            var writer = new XmlTextWriter(sw) { Formatting = Formatting.Indented };
            writer.WriteProcessingInstruction("xml", "version=\"1.0\" encoding=\"" + game.TextEncoding.WebName + "\"");
            writer.WriteStartElement(XmlRootNodeName);
            writer.WriteAttributeString("Version", LatestXmlVersion);
            writer.WriteAttributeString("VersionIndex", LatestXmlVersionIndex.ToString());
            writer.WriteAttributeString("EditorVersion", AGS.Types.Version.AGS_EDITOR_VERSION);
            game.SavedXmlVersion = LatestXmlVersion;
            game.SavedXmlVersionIndex = LatestXmlVersionIndex;
            game.SavedXmlEncodingCodePage = game.TextEncoding.CodePage;
            game.ToXml(writer);
            writer.WriteEndElement();
            writer.Flush();
            return sw.ToString();
        }

        [Fact]
        public void LoadsReferenceGame()
        {
            var doc = new XmlDocument();
            doc.Load(ReferenceGameFile());
            Game game = LoadGame(doc);

            Assert.Equal("Bernard-Starterpack", game.Settings.GameName);
            Assert.Equal(GameColorDepth.HighColor, game.Settings.ColorDepth);
            Assert.Equal(new System.Drawing.Size(320, 200), game.Settings.CustomResolution);

            Assert.Equal(21, game.Characters.Count);
            Assert.Equal(21, game.Rooms.Count);
            Assert.Equal(13, game.GUIs.Count);
            Assert.Equal(6, game.InventoryItems.Count);
            Assert.Equal(10, game.Cursors.Count);
            Assert.Equal(15, game.Fonts.Count);
            Assert.Equal(3, game.Translations.Count);
            Assert.Equal(122, game.AudioClips.Count);
            Assert.Equal(46, game.RootViewFolder.AllItemsFlat.Count());
            Assert.Equal(592, game.RootSpriteFolder.CountSpritesInAllSubFolders());
            Assert.Equal(
                new[] { "CharacterChange", "Credits", "Doors", "GlobalScript" },
                game.RootScriptFolder.AllItemsFlat
                    .Select(s => Path.GetFileNameWithoutExtension(s.Header.FileName))
                    .OrderBy(n => n, StringComparer.Ordinal));
            Assert.Equal(4, game.PlayerCharacter.ID);
            Assert.Equal("Windows, Linux", game.Settings.BuildTargets);

            // "ANSI" must mean the Windows ANSI code page, not .NET's UTF-8 Encoding.Default.
            Assert.Equal("ANSI", game.Settings.GameTextEncoding);
            Assert.Equal(AnsiEncoding.DefaultCodePage, game.TextEncoding.CodePage);
        }

        [Fact]
        public void SaveIsStableAcrossReload()
        {
            var original = new XmlDocument();
            original.Load(ReferenceGameFile());
            string firstSave = SaveGame(LoadGame(original));

            var reloaded = new XmlDocument();
            reloaded.LoadXml(firstSave);
            string secondSave = SaveGame(LoadGame(reloaded));

            Assert.Equal(firstSave, secondSave);
        }
    }
}
