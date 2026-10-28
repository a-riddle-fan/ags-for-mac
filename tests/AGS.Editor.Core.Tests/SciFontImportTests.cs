using System;
using System.Collections.Generic;
using System.IO;
using AGS.Editor.Hosting;
using AGS.Native;
using AGS.Types;

namespace AGS.Editor.Core.Tests
{
    /// <summary>Sierra SCI fonts imported over a game font (upstream ImportSCIFont: converted to WFN).</summary>
    [Collection(CurrentDirectoryCollection.Name)]
    public class SciFontImportTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "ags4mac-scifont-" + Guid.NewGuid().ToString("N"));

        public SciFontImportTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        // (width, height, pixel rows of (width / 8 + 1) bytes each), as stored in the SCI file
        private static readonly (int W, int H, byte[] Pixels)[] Chars =
        {
            (0, 0, Array.Empty<byte>()),                    // empty character
            (3, 2, new byte[] { 0b1110_0000, 0b1010_0000 }),
            (9, 1, new byte[] { 0xFF, 0x80 }),              // two bytes per row
        };

        // An SCI font as upstream's import_sci_font reads it: 0x87, 3 skipped bytes, character count,
        // line height, the offsets (2 less than the position of the character data), then per character
        // its width + 1, height and pixel rows.
        private static byte[] SciFont()
        {
            var data = new List<byte> { 0x87, 0, 0, 0 };
            void Int16(int v) { data.Add((byte)v); data.Add((byte)(v >> 8)); }
            Int16(Chars.Length);
            Int16(8);
            int pos = data.Count + 2 * Chars.Length;
            foreach (var c in Chars)
            {
                Int16(pos - 2);
                pos += 2 + c.Pixels.Length;
            }
            foreach (var c in Chars)
            {
                data.Add((byte)(c.W + 1));
                data.Add((byte)c.H);
                data.AddRange(c.Pixels);
            }
            return data.ToArray();
        }

        // The WFN file upstream writes for it: signature, table position, per character width + 1,
        // height and pixel rows (16-bit values), then the table of character positions.
        private static byte[] ExpectedWfn()
        {
            var data = new List<byte>();
            void Int16(int v) { data.Add((byte)v); data.Add((byte)(v >> 8)); }
            data.AddRange(System.Text.Encoding.ASCII.GetBytes("WGT Font File  "));
            Int16(0);
            var offsets = new List<int>();
            foreach (var c in Chars)
            {
                offsets.Add(data.Count);
                Int16(c.W + 1);
                Int16(c.H);
                data.AddRange(c.Pixels);
            }
            int table = data.Count;
            offsets.ForEach(Int16);
            data[15] = (byte)table;
            data[16] = (byte)(table >> 8);
            return data.ToArray();
        }

        [Fact]
        public void SciFontIsConvertedToWfnAsUpstreamDoes()
        {
            string sci = Path.Combine(_dir, "FONT.001");
            string wfn = Path.Combine(_dir, "out.wfn");
            File.WriteAllBytes(sci, SciFont());

            NativeGuiRenderer.ImportSciFont(sci, wfn);

            Assert.Equal(ExpectedWfn(), File.ReadAllBytes(wfn));
        }

        [Fact]
        public void OtherFilesAreRejectedWithUpstreamsMessage()
        {
            string notSci = Path.Combine(_dir, "font.txt");
            File.WriteAllText(notSci, "hello");
            var ex = Assert.Throws<AGSEditorException>(() => NativeGuiRenderer.ImportSciFont(notSci, Path.Combine(_dir, "x.wfn")));
            Assert.Equal("Not a valid SCI font file", ex.Message);

            ex = Assert.Throws<AGSEditorException>(() => NativeGuiRenderer.ImportSciFont(Path.Combine(_dir, "missing"), Path.Combine(_dir, "x.wfn")));
            Assert.Equal("File not found", ex.Message);
        }

        [Fact]
        public void ImportingAnSciFontReplacesATrueTypeFont()
        {
            string gameDir = Path.Combine(_dir, "game");
            CopyDirectory(ReferenceGame.Directory, gameDir);
            string cwd = Directory.GetCurrentDirectory();
            try
            {
                GameProject project = GameProject.Load(Path.Combine(gameDir, "Game.agf"), new ConsoleEditorHost(TextWriter.Null));
                Font font = project.Game.Fonts[3];
                Assert.True(File.Exists(Path.Combine(gameDir, "agsfnt3.ttf")));
                string sci = Path.Combine(_dir, "FONT.000");
                File.WriteAllBytes(sci, SciFont());

                FontOperations.ImportFont(project, font, sci);

                Assert.Equal("agsfnt3.wfn", font.ProjectFilename);
                Assert.Equal(0, font.PointSize);
                Assert.False(File.Exists(Path.Combine(gameDir, "agsfnt3.ttf")));
                Assert.Equal(ExpectedWfn(), File.ReadAllBytes(Path.Combine(gameDir, "agsfnt3.wfn")));
                Assert.EndsWith("FONT.000", font.SourceFilename); // outside the project: kept as an absolute path

                // The converted font loads as a bitmap font with the SCI font's characters.
                NativeGuiRenderer.Init(project.Game, project.Directory);
                var metrics = NativeGuiRenderer.GetFontMetrics(3);
                Assert.True(metrics.IsBitmapFont);
                Assert.Equal(Chars.Length - 1, metrics.TopmostChar);
            }
            finally
            {
                Directory.SetCurrentDirectory(cwd);
            }
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
