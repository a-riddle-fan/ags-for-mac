using System;
using System.IO;
using System.Linq;
using AGS.Editor.Hosting;
using AGS.Imaging;
using AGS.Native;
using AGS.Types;

namespace AGS.Editor.Core.Tests
{
    /// <summary>GUIs drawn by upstream's GUI code with the game's fonts and sprites (libagsnative).</summary>
    [Collection(CurrentDirectoryCollection.Name)]
    public class GuiRenderTests
    {
        [Fact]
        public void DrawsEveryGuiOfTheReferenceGame()
        {
            string cwd = Directory.GetCurrentDirectory();
            try
            {
                GameProject project = GameProject.Load(Path.Combine(ReferenceGame.Directory, "Game.agf"), new ConsoleEditorHost(TextWriter.Null));
                NativeGuiRenderer.Init(project.Game, project.Directory);
                string outDir = Path.Combine(Path.GetTempPath(), "ags4mac-guis");
                Directory.CreateDirectory(outDir);
                foreach (GUI gui in project.Game.RootGUIFolder.AllItemsFlat)
                {
                    using PixelImage image = NativeGuiRenderer.Draw(gui);
                    Assert.Equal((gui.EditorWidth, gui.EditorHeight), (image.Width, image.Height));
                    new NativeImageCodec().Encode(image, Path.Combine(outDir, $"gui{gui.ID}-{gui.Name}.png"));
                }
                // The verb GUI has opaque pixels (its button images).
                GUI verbs = project.Game.RootGUIFolder.AllItemsFlat.OfType<NormalGUI>().OrderByDescending(g => g.Controls.OfType<GUIButton>().Count()).First();
                using PixelImage verbImage = NativeGuiRenderer.Draw(verbs);
                Assert.Contains(Enumerable.Range(0, verbImage.Height).SelectMany(y => verbImage.GetRow(y).ToArray().Where((b, i) => i % 4 == 3)), a => a == 255);
            }
            finally
            {
                Directory.SetCurrentDirectory(cwd);
            }
        }
    }
}
