using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AGS.Editor.Build;
using AGS.Editor.Hosting;
using AGS.Native;
using AGS.Types;

namespace AGS.Editor.Core.Tests
{
    [Collection(CurrentDirectoryCollection.Name)]
    public class CustomPropertyRenameTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-cprename-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        [Fact]
        public void RenamedAndRemovedPropertiesAreAppliedToCharactersAndRooms()
        {
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            KnownBuildTargets.RegisterAll();
            GameProject project = GameProject.Load(Path.Combine(_gameDir, "Game.agf"), new ConsoleEditorHost(TextWriter.Null));
            Game game = project.Game;
            game.PropertySchema.PropertyDefinitions.Add(new CustomPropertySchemaItem { Name = "Weight", Type = CustomPropertyType.Number, DefaultValue = "0" });
            game.PropertySchema.PropertyDefinitions.Add(new CustomPropertySchemaItem { Name = "Old", Type = CustomPropertyType.Text, DefaultValue = "" });
            Character character = game.RootCharacterFolder.AllItemsFlat.First();
            character.Properties.PropertyValues["Weight"] = new CustomProperty("Weight", "42");
            character.Properties.PropertyValues["Old"] = new CustomProperty("Old", "gone");

            // a value on a hotspot of room 2
            UnloadedRoom unloaded = game.RootRoomFolder.AllItemsFlat.Cast<UnloadedRoom>().Single(r => r.Number == 2);
            string roomFile = Path.Combine(_gameDir, unloaded.FileName);
            using (NativeRoom native = NativeRoom.Load(roomFile))
            {
                Room room = native.ToRoom(unloaded, game.TextEncoding);
                room.Hotspots[1].Properties.PropertyValues["Weight"] = new CustomProperty("Weight", "7");
                native.ApplyRoom(room, game.TextEncoding);
                native.Save(roomFile);
            }

            var newSchema = new CustomPropertySchema(game.PropertySchema);
            newSchema.PropertyDefinitions.Single(p => p.Name == "Weight").Name = "Mass";
            newSchema.PropertyDefinitions.RemoveAll(p => p.Name == "Old");
            var changes = new List<CustomPropertyDefChange>
            {
                new CustomPropertyDefChange(CustomPropertyDefChangeType.Edit, "Weight", "Mass"),
                new CustomPropertyDefChange(CustomPropertyDefChangeType.Remove, "Old", null),
            };
            Assert.True(CustomPropertySchemaChanges.HasRenames(changes));
            CompileMessages errors = CustomPropertySchemaChanges.Apply(project, newSchema, changes);
            Assert.False(errors.HasErrors, string.Join("\n", errors.Select(m => m.Message)));

            Assert.Equal(new[] { "Mass" }, game.PropertySchema.PropertyDefinitions.Select(p => p.Name).Where(n => n == "Mass" || n == "Weight" || n == "Old"));
            Assert.Equal("42", character.Properties.PropertyValues["Mass"].Value);
            Assert.False(character.Properties.PropertyValues.ContainsKey("Weight"));
            Assert.False(character.Properties.PropertyValues.ContainsKey("Old"));
            using (NativeRoom native = NativeRoom.Load(roomFile))
            {
                Room room = native.ToRoom(unloaded, game.TextEncoding);
                Assert.Equal("7", room.Hotspots[1].Properties.PropertyValues["Mass"].Value);
                Assert.False(room.Hotspots[1].Properties.PropertyValues.ContainsKey("Weight"));
            }

            project.Save();
            BuildResult result = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory).Build(project.GameFilePath);
            Assert.True(result.Succeeded, ReferenceGame.BuildFailure(result));
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
