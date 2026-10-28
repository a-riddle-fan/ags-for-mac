using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using AGS.Editor.Build;
using AGS.Editor.Hosting;
using AGS.Imaging;
using AGS.Native;
using AGS.Types;

namespace AGS.Editor.Core.Tests
{
    /// <summary>
    /// Sprite import through upstream's spriteimport tool, merged into the reference game's sprite file.
    /// Needs the tools from scripts/build-upstream-tools.sh (build/agstools).
    /// </summary>
    [Collection(CurrentDirectoryCollection.Name)]
    public class SpriteImportTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-sprimport-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            if (Directory.Exists(_gameDir)) Directory.Delete(_gameDir, true);
        }

        [Fact]
        public void ImportedPngBecomesNewSpriteAndOtherSpritesStayUnchanged()
        {
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            KnownBuildTargets.RegisterAll();
            string agf = Path.Combine(_gameDir, "Game.agf");
            GameProject project = GameProject.Load(agf, new ConsoleEditorHost(TextWriter.Null));
            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            Assert.True(toolchain != null, "agsbuild not found; run scripts/build-upstream-tools.sh");

            // A 7x5 image: red left half, blue right half (exact in the game's 16-bit format).
            string png = Path.Combine(_gameDir, "newsprite.png");
            using (var image = new PixelImage(7, 5, ImagePixelFormat.Bgra32))
            {
                for (int y = 0; y < 5; y++)
                {
                    Span<byte> row = image.GetRow(y);
                    for (int x = 0; x < 7; x++)
                    {
                        bool red = x < 4;
                        row[x * 4 + 0] = red ? (byte)0 : (byte)255;   // B
                        row[x * 4 + 1] = 0;                            // G
                        row[x * 4 + 2] = red ? (byte)255 : (byte)0;   // R
                        row[x * 4 + 3] = 255;                          // A
                    }
                }
                new NativeImageCodec().Encode(image, png);
            }

            int expectedSlot = SpriteImporter.FindFreeSlot(project.Game);
            int[] samples = project.Game.RootSpriteFolder.GetAllSpritesFromAllSubFolders().Select(s => s.Number).Take(20).ToArray();
            byte[][] before = samples.Select(ReadSpritePixels).ToArray();

            Sprite sprite = new SpriteImporter(project, toolchain).Import(png);

            Assert.Equal(expectedSlot, sprite.Number);
            Assert.Equal(7, sprite.Width);
            Assert.Equal(5, sprite.Height);
            Assert.Equal(16, sprite.ColorDepth);
            Assert.Equal("newsprite.png", sprite.SourceFile);
            Assert.Contains(sprite, project.Game.RootSpriteFolder.Sprites);

            using (SpriteFile file = SpriteFile.OpenInProject(_gameDir))
            using (PixelImage imported = file.LoadSprite(sprite.Number))
            {
                Assert.Equal(7, imported.Width);
                Assert.Equal(ImagePixelFormat.Rgb565, imported.PixelFormat);
                Assert.Equal(0xF800, BitConverter.ToUInt16(imported.GetRow(2).Slice(0, 2)));
                Assert.Equal(0x001F, BitConverter.ToUInt16(imported.GetRow(2).Slice(6 * 2, 2)));
            }
            for (int i = 0; i < samples.Length; i++)
                Assert.Equal(before[i], ReadSpritePixels(samples[i]));

            // The project saves, reloads with the new sprite and still builds.
            project.Save();
            GameProject reloaded = GameProject.Load(agf, new ConsoleEditorHost(TextWriter.Null));
            Sprite again = reloaded.Game.RootSpriteFolder.FindSpriteByID(expectedSlot, true);
            Assert.NotNull(again);
            Assert.Equal(7, again.Width);
            BuildResult result = toolchain.Build(agf);
            Assert.True(result.Succeeded, ReferenceGame.BuildFailure(result));
        }

        [Fact]
        public void TopLeftPixelTransparencyTurnsThatColourIntoMagenta()
        {
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            KnownBuildTargets.RegisterAll();
            GameProject project = GameProject.Load(Path.Combine(_gameDir, "Game.agf"), new ConsoleEditorHost(TextWriter.Null));
            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            Assert.True(toolchain != null, "agsbuild not found; run scripts/build-upstream-tools.sh");

            // White background with a blue pixel in the middle.
            string png = Path.Combine(_gameDir, "white.png");
            using (var image = new PixelImage(3, 3, ImagePixelFormat.Bgra32))
            {
                image.Pixels.Fill(255);
                Span<byte> mid = image.GetRow(1).Slice(4, 4);
                mid[1] = 0; mid[2] = 0; // B=255, G=0, R=0
                new NativeImageCodec().Encode(image, png);
            }
            Sprite sprite = new SpriteImporter(project, toolchain).Import(png, transparency: SpriteImportTransparency.TopLeft);
            Assert.Equal(SpriteImportTransparency.TopLeft, sprite.TransparentColour);
            using SpriteFile file = SpriteFile.OpenInProject(_gameDir);
            using PixelImage imported = file.LoadSprite(sprite.Number);
            Assert.Equal(0xF81F, BitConverter.ToUInt16(imported.GetRow(0).Slice(0, 2)));   // white -> transparent
            Assert.Equal(0x001F, BitConverter.ToUInt16(imported.GetRow(1).Slice(2, 2)));   // blue stays
        }

        [Fact]
        public void ExportedSpriteReimportsWithIdenticalPixels()
        {
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            KnownBuildTargets.RegisterAll();
            GameProject project = GameProject.Load(Path.Combine(_gameDir, "Game.agf"), new ConsoleEditorHost(TextWriter.Null));
            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            Assert.True(toolchain != null, "agsbuild not found; run scripts/build-upstream-tools.sh");

            const int original = 263; // 16-bit character frame with magenta (transparent) background
            string png = Path.Combine(_gameDir, "frame.png");
            using (SpriteFile file = SpriteFile.OpenInProject(_gameDir))
            using (PixelImage image = file.LoadSprite(original))
                new NativeImageCodec().Encode(image, png);

            Sprite sprite = new SpriteImporter(project, toolchain).Import(png);

            byte[] expected = ReadSpritePixels(original);
            byte[] actual = ReadSpritePixels(sprite.Number);
            int firstDiff = Enumerable.Range(0, Math.Min(expected.Length, actual.Length)).FirstOrDefault(i => expected[i] != actual[i], -1);
            Assert.True(firstDiff < 0 && expected.Length == actual.Length,
                $"first difference at byte {firstDiff}: expected {(firstDiff >= 0 ? expected[firstDiff] : 0):x2}, got {(firstDiff >= 0 ? actual[firstDiff] : 0):x2}; lengths {expected.Length}/{actual.Length}");
        }

        [Fact]
        public void DeletesUnusedSpritesOnlyAndKeepsTheOthers()
        {
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            KnownBuildTargets.RegisterAll();
            string agf = Path.Combine(_gameDir, "Game.agf");
            GameProject project = GameProject.Load(agf, new ConsoleEditorHost(TextWriter.Null));
            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            Assert.True(toolchain != null, "agsbuild not found; run scripts/build-upstream-tools.sh");

            // A sprite used in a view frame is refused with upstream's usage report.
            int usedNumber = project.Game.RootViewFolder.AllItemsFlat.SelectMany(v => v.Loops).SelectMany(l => l.Frames).First(f => f.Image > 0).Image;
            Sprite used = project.Game.RootSpriteFolder.FindSpriteByID(usedNumber, true);
            var problems = SpriteOperations.DeleteSprites(project.Game, _gameDir, new[] { used });
            Assert.Contains("is used in the following places", Assert.Single(problems));
            Assert.NotNull(project.Game.RootSpriteFolder.FindSpriteByID(usedNumber, true));

            // An imported (unused) sprite is removed and its slot emptied; the others stay byte-identical.
            string png = Path.Combine(_gameDir, "frame.png");
            using (SpriteFile file = SpriteFile.OpenInProject(_gameDir))
            using (PixelImage image = file.LoadSprite(usedNumber))
                new NativeImageCodec().Encode(image, png);
            Sprite imported = new SpriteImporter(project, toolchain).Import(png);
            byte[] usedPixels = ReadSpritePixels(usedNumber);

            Assert.Empty(SpriteOperations.DeleteSprites(project.Game, _gameDir, new[] { imported }));
            Assert.Null(project.Game.RootSpriteFolder.FindSpriteByID(imported.Number, true));
            using (SpriteFile file = SpriteFile.OpenInProject(_gameDir))
                Assert.False(file.GetInfo(imported.Number).Exists);
            Assert.Equal(usedPixels, ReadSpritePixels(usedNumber));

            project.Save();
            { var built = toolchain.Build(agf); Assert.True(built.Succeeded, ReferenceGame.BuildFailure(built)); }
        }

        [Fact]
        public void TiledImportCutsOneSpritePerTileAndReimportsThem()
        {
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            KnownBuildTargets.RegisterAll();
            string agf = Path.Combine(_gameDir, "Game.agf");
            GameProject project = GameProject.Load(agf, new ConsoleEditorHost(TextWriter.Null));
            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);

            // A 14x4 sheet: red, green, blue 4x4 tiles with 1 pixel of white between them.
            (byte R, byte G, byte B)[] colours = { (255, 0, 0), (0, 255, 0), (0, 0, 255) };
            string png = Path.Combine(_gameDir, "sheet.png");
            using (var image = new PixelImage(14, 4, ImagePixelFormat.Bgra32))
            {
                image.Pixels.Fill(255);
                for (int t = 0; t < 3; t++)
                    for (int y = 0; y < 4; y++)
                        for (int x = 0; x < 4; x++)
                        {
                            Span<byte> px = image.GetRow(y).Slice((t * 5 + x) * 4, 4);
                            px[0] = colours[t].B; px[1] = colours[t].G; px[2] = colours[t].R; px[3] = 255;
                        }
                new NativeImageCodec().Encode(image, png);
            }
            var sheet = new AGS.Editor.Utils.SpriteSheet(new System.Drawing.Point(0, 0), new System.Drawing.Size(4, 4),
                new System.Drawing.Size(1, 0), SpriteImportTilingDirection.Right, 10);
            var sprites = new SpriteImporter(project, toolchain).ImportTiles(png, sheet);

            Assert.Equal(3, sprites.Count); // only three tiles fit
            ushort[] expected = { 0xF800, 0x07E0, 0x001F };
            for (int t = 0; t < 3; t++)
            {
                Assert.Equal((4, 4, true, t * 5), (sprites[t].Width, sprites[t].Height, sprites[t].ImportAsTile, sprites[t].OffsetX));
                using SpriteFile file = SpriteFile.OpenInProject(_gameDir);
                using PixelImage tile = file.LoadSprite(sprites[t].Number);
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                        Assert.Equal(expected[t], BitConverter.ToUInt16(tile.GetRow(y).Slice(x * 2, 2)));
            }

            // A second row of tiles (OffsetY > 0, native patch 0009): the sheet's lower half.
            var lower = new SpriteImporter(project, toolchain).ImportTiles(png, new AGS.Editor.Utils.SpriteSheet(
                new System.Drawing.Point(0, 2), new System.Drawing.Size(4, 2), new System.Drawing.Size(1, 0), SpriteImportTilingDirection.Right, 10));
            using (SpriteFile file = SpriteFile.OpenInProject(_gameDir))
            using (PixelImage tile = file.LoadSprite(lower[2].Number))
                Assert.Equal(expected[2], BitConverter.ToUInt16(tile.GetRow(1).Slice(2, 2)));

            // Reimport keeps each sprite's tile.
            new SpriteImporter(project, toolchain).Reimport(sprites[1]);
            using (SpriteFile file = SpriteFile.OpenInProject(_gameDir))
            using (PixelImage tile = file.LoadSprite(sprites[1].Number))
                Assert.Equal((ushort)0x07E0, BitConverter.ToUInt16(tile.GetRow(2).Slice(4, 2)));

            project.Save();
            { var built = toolchain.Build(agf); Assert.True(built.Succeeded, ReferenceGame.BuildFailure(built)); }
        }

        [Fact]
        public void ReplaceFromFileAndRestoreAllFromSources()
        {
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            KnownBuildTargets.RegisterAll();
            string agf = Path.Combine(_gameDir, "Game.agf");
            GameProject project = GameProject.Load(agf, new ConsoleEditorHost(TextWriter.Null));
            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            string Solid(string name, int w, byte r, byte g, byte b)
            {
                string file = Path.Combine(_gameDir, name);
                using var image = new PixelImage(w, 3, ImagePixelFormat.Bgra32);
                for (int y = 0; y < 3; y++)
                    for (int x = 0; x < w; x++)
                    {
                        Span<byte> px = image.GetRow(y).Slice(x * 4, 4);
                        px[0] = b; px[1] = g; px[2] = r; px[3] = 255;
                    }
                new NativeImageCodec().Encode(image, file);
                return file;
            }
            var importer = new SpriteImporter(project, toolchain);
            Sprite sprite = importer.Import(Solid("red.png", 4, 255, 0, 0));
            int number = sprite.Number;
            int[] others = project.Game.RootSpriteFolder.GetAllSpritesFromAllSubFolders().Select(x => x.Number).Where(n => n != number).Take(15).ToArray();
            byte[][] before = others.Select(ReadSpritePixels).ToArray();

            // Replace from file: same number, the new image and source
            importer.Replace(sprite, Solid("blue.png", 6, 0, 0, 255), SpriteImportTransparency.LeaveAsIs, true);
            Assert.Equal((number, 6, "blue.png"), (sprite.Number, sprite.Width, sprite.SourceFile));
            using (SpriteFile file = SpriteFile.OpenInProject(_gameDir))
            using (PixelImage image = file.LoadSprite(number))
                Assert.Equal(0x001F, BitConverter.ToUInt16(image.GetRow(1).Slice(2, 2)));

            // Restore all from sources: the changed source file is picked up; sprites without sources stay as they are
            Solid("blue.png", 5, 0, 255, 0);
            var all = project.Game.RootSpriteFolder.GetAllSpritesFromAllSubFolders().ToList();
            var skipped = importer.RestoreFromSources(all);
            Assert.DoesNotContain(sprite, skipped);
            Assert.Equal(5, sprite.Width);
            using (SpriteFile file = SpriteFile.OpenInProject(_gameDir))
            using (PixelImage image = file.LoadSprite(number))
                Assert.Equal(0x07E0, BitConverter.ToUInt16(image.GetRow(1).Slice(2, 2)));
            for (int i = 0; i < others.Length; i++)
                if (skipped.Any(x => x.Number == others[i]))
                    Assert.Equal(before[i], ReadSpritePixels(others[i]));

            project.Save();
            { var built = toolchain.Build(agf); Assert.True(built.Succeeded, ReferenceGame.BuildFailure(built)); }
        }

        [Fact]
        public void ExportAllSpritesAsSourcesAndRestoreGivesTheSamePixels()
        {
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            KnownBuildTargets.RegisterAll();
            NativeImageCodec.RegisterAll();
            string agf = Path.Combine(_gameDir, "Game.agf");
            GameProject project = GameProject.Load(agf, new ConsoleEditorHost(TextWriter.Null));
            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            var all = project.Game.RootSpriteFolder.GetAllSpritesFromAllSubFolders().ToList();
            // sprites in the game's colour depth (an 8-bit sprite in a 16-bit game is converted on reimport, as upstream)
            int gameDepth = (int)project.Game.Settings.ColorDepth * 8;
            List<Sprite> sample;
            using (SpriteFile file = SpriteFile.OpenInProject(_gameDir))
                sample = all.Where(sp => file.GetInfo(sp.Number).Exists && file.GetInfo(sp.Number).ColorDepth == gameDepth).Take(25).ToList();
            Assert.True(sample.Count >= 20);
            byte[][] before = sample.Select(sp => ReadSpritePixels(sp.Number)).ToArray();

            int exported;
            using (SpriteFile file = SpriteFile.OpenInProject(_gameDir))
                exported = SpriteExport.ExportSprites(project, file, project.Game.RootSpriteFolder,
                    new SpriteExport.ExportSpritesOptions(Path.Combine("exported", "spr%Number%"), true, 0, updateSourcePath: true, resetTileSettings: true));
            Assert.Equal(all.Count, exported);
            Sprite first = sample[0];
            Assert.Equal(Path.Combine("exported", $"spr{first.Number}.{(first.ColorDepth < 32 && !first.AlphaChannel ? "bmp" : "png")}").Replace('/', '\\'), first.SourceFile);
            Assert.True(File.Exists(Path.Combine(_gameDir, "exported", Path.GetFileName(ProjectPaths.ToLocal(first.SourceFile)))));

            // skip if the source is valid: nothing left to export now
            using (SpriteFile file = SpriteFile.OpenInProject(_gameDir))
                Assert.Equal(0, SpriteExport.ExportSprites(project, file, project.Game.RootSpriteFolder,
                    new SpriteExport.ExportSpritesOptions("again/%Number%", true, SpriteExport.SkipIf.SourceValid, false, false)));

            // the exported files reimport to identical sprites
            Assert.Empty(new SpriteImporter(project, toolchain).RestoreFromSources(sample));
            for (int i = 0; i < sample.Count; i++)
            {
                byte[] after = ReadSpritePixels(sample[i].Number);
                int d = Enumerable.Range(0, Math.Min(before[i].Length, after.Length)).FirstOrDefault(k => before[i][k] != after[k], -1);
                Assert.True(before[i].SequenceEqual(after), $"sprite {sample[i].Number} ({sample[i].ColorDepth}-bit, {sample[i].TransparentColour}, {sample[i].Width}x{sample[i].Height}) changed at byte {d}: {(d >= 0 ? before[i][d] : 0):x2}->{(d >= 0 ? after[d] : 0):x2}, lengths {before[i].Length}/{after.Length}");
            }
        }

        [Fact]
        public void CropSpriteEdgesKeepsTheVisibleAreaAndRemembersTheTile()
        {
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            KnownBuildTargets.RegisterAll();
            string agf = Path.Combine(_gameDir, "Game.agf");
            GameProject project = GameProject.Load(agf, new ConsoleEditorHost(TextWriter.Null));
            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);
            // 10x8, magenta border: 2 columns left, 1 right, 3 rows on top; red inside
            string png = Path.Combine(_gameDir, "framed.png");
            using (var image = new PixelImage(10, 8, ImagePixelFormat.Bgra32))
            {
                for (int y = 0; y < 8; y++)
                    for (int x = 0; x < 10; x++)
                    {
                        bool inside = x >= 2 && x <= 8 && y >= 3;
                        Span<byte> px = image.GetRow(y).Slice(x * 4, 4);
                        px[0] = inside ? (byte)0 : (byte)255; px[1] = 0; px[2] = 255; px[3] = 255;
                    }
                new NativeImageCodec().Encode(image, png);
            }
            var importer = new SpriteImporter(project, toolchain);
            Sprite a = importer.Import(png);
            Sprite b = importer.Import(png);

            Assert.Equal("All sprites to be cropped must have the same dimensions. If you want to crop unrelated sprites, do them separately.",
                SpriteOperations.CropSpriteEdges(_gameDir, new[] { a, project.Game.RootSpriteFolder.FindSpriteByID(1, true) }, false));
            Assert.Null(SpriteOperations.CropSpriteEdges(_gameDir, new[] { a, b }, false));
            Assert.Equal((7, 5, true, 2, 3, 7, 5), (a.Width, a.Height, a.ImportAsTile, a.OffsetX, a.OffsetY, a.ImportWidth, a.ImportHeight));
            using (SpriteFile file = SpriteFile.OpenInProject(_gameDir))
            using (PixelImage cropped = file.LoadSprite(b.Number))
            {
                Assert.Equal((7, 5), (cropped.Width, cropped.Height));
                Assert.Equal(0xF800, BitConverter.ToUInt16(cropped.GetRow(0).Slice(0, 2)));
            }
            Assert.Equal("The selected sprites could not be cropped any further.", SpriteOperations.CropSpriteEdges(_gameDir, new[] { a, b }, false));

            // reimporting from the source cuts the same tile
            byte[] before = ReadSpritePixels(a.Number);
            importer.Reimport(a);   // needs native patch 0009 (CopyPixelsRegion with a y offset)
            Assert.Equal(before, ReadSpritePixels(a.Number));

            // symmetric: as much is cut on the left as on the right
            Sprite c = importer.Import(png);
            Assert.Null(SpriteOperations.CropSpriteEdges(_gameDir, new[] { c }, true));
            Assert.Equal((8, 5, 1), (c.Width, c.Height, c.OffsetX));

            project.Save();
            { var built = toolchain.Build(agf); Assert.True(built.Succeeded, ReferenceGame.BuildFailure(built)); }
        }

        [Fact]
        public void ReplaceSourcePathsMovesWindowsAndRelativePaths()
        {
            var game = new Game();
            var a = new Sprite(1, 1, 1) { SourceFile = @"C:\Users\Me\Art\hero.png" };
            var b = new Sprite(2, 1, 1) { SourceFile = @"C:\users\me\art\sub\door.bmp" };
            var c = new Sprite(3, 1, 1) { SourceFile = @"Other\x.png" };
            var d = new Sprite(4, 1, 1) { SourceFile = "" };
            foreach (Sprite sp in new[] { a, b, c, d }) game.RootSpriteFolder.Sprites.Add(sp);

            Assert.Equal(2, SpriteOperations.ReplaceSourcePaths(game, @"C:\Users\Me\Art", "Sprites"));
            Assert.Equal(@"Sprites\hero.png", a.SourceFile);
            Assert.Equal(@"Sprites\sub\door.bmp", b.SourceFile);
            Assert.Equal(@"Other\x.png", c.SourceFile);
            Assert.Equal(1, SpriteOperations.ReplaceSourcePaths(game, "Other", "/Users/me/Art"));
            Assert.Equal("/Users/me/Art/x.png", c.SourceFile);
            Assert.Equal(0, SpriteOperations.ReplaceSourcePaths(game, "Nowhere", "Else"));
        }

        [Fact]
        public void RenumberSpritesDeleteFoldersAndAssignToViews()
        {
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            KnownBuildTargets.RegisterAll();
            string agf = Path.Combine(_gameDir, "Game.agf");
            GameProject project = GameProject.Load(agf, new ConsoleEditorHost(TextWriter.Null));
            Game game = project.Game;
            AgsBuildToolchain toolchain = AgsBuildToolchain.Locate(searchFrom: AppContext.BaseDirectory);

            // an unused sprite in a new sub-folder
            int usedNumber = game.RootViewFolder.AllItemsFlat.SelectMany(v => v.Loops).SelectMany(l => l.Frames).First(f => f.Image > 0).Image;
            string png = Path.Combine(_gameDir, "frame.png");
            using (SpriteFile file = SpriteFile.OpenInProject(_gameDir))
            using (PixelImage image = file.LoadSprite(usedNumber))
                new NativeImageCodec().Encode(image, png);
            var folder = new SpriteFolder("Imported");
            game.RootSpriteFolder.SubFolders.Add(folder);
            Sprite sprite = new SpriteImporter(project, toolchain).Import(png, folder);
            int oldNumber = sprite.Number;
            byte[] pixels = ReadSpritePixels(oldNumber);

            // Change sprite number: the image moves, the old slot is empty, a used number is refused
            int target = SpriteOperations.FindNearestFreeSpriteNumber(game, 5000);
            Assert.Equal(5000, target);
            Assert.Throws<AGSEditorException>(() => SpriteOperations.ChangeSpriteNumbers(game, _gameDir, new[] { sprite }, new[] { usedNumber }));
            SpriteOperations.ChangeSpriteNumbers(game, _gameDir, new[] { sprite }, new[] { target });
            Assert.Equal(target, sprite.Number);
            Assert.Equal(pixels, ReadSpritePixels(target));
            using (SpriteFile file = SpriteFile.OpenInProject(_gameDir))
                Assert.False(file.GetInfo(oldNumber).Exists);
            Assert.Equal(new[] { 6000, 6001 }, SpriteOperations.SequentialNumbers(game, new[] { sprite, game.RootSpriteFolder.FindSpriteByID(usedNumber, true) }, 6000));

            // Assign to view: a new loop frame with the sprite
            View view = game.RootViewFolder.AllItemsFlat.First();
            int loops = view.Loops.Count;
            string message = SpriteOperations.AssignSpritesToView(game, new System.Collections.Generic.List<int> { target }, view.ID, loops, false, false, false, true);
            Assert.Equal("The selected sprites were assigned successfully.", message);
            ViewFrame frame = Assert.Single(view.Loops[loops].Frames);
            Assert.Equal((target, true), (frame.Image, frame.Flipped));

            // Delete folder: refused while its sprite is used (by the view now), allowed after removing the use
            Assert.Throws<AGSEditorException>(() => SpriteOperations.DeleteSpriteFolder(game, _gameDir, folder));
            view.Loops.RemoveAt(loops);
            SpriteOperations.DeleteSpriteFolder(game, _gameDir, folder);
            Assert.DoesNotContain(folder, game.RootSpriteFolder.SubFolders);
            Assert.Null(game.RootSpriteFolder.FindSpriteByID(target, true));
            using (SpriteFile file = SpriteFile.OpenInProject(_gameDir))
                Assert.False(file.GetInfo(target).Exists);

            project.Save();
            { var built = toolchain.Build(agf); Assert.True(built.Succeeded, ReferenceGame.BuildFailure(built)); }
        }

        private byte[] ReadSpritePixels(int slot)
        {
            using SpriteFile file = SpriteFile.OpenInProject(_gameDir);
            using PixelImage image = file.LoadSprite(slot);
            return image == null ? Array.Empty<byte>() : image.Pixels.ToArray();
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
