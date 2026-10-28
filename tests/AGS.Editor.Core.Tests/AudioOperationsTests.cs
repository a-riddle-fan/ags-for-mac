using System;
using System.IO;
using System.Linq;
using AGS.Editor.Hosting;
using AGS.Types;

namespace AGS.Editor.Core.Tests
{
    [Collection(CurrentDirectoryCollection.Name)]
    public class AudioOperationsTests : IDisposable
    {
        private readonly string _gameDir = Path.Combine(Path.GetTempPath(), "ags4mac-audio-" + Guid.NewGuid().ToString("N"));
        private readonly string _originalCwd = Directory.GetCurrentDirectory();

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_originalCwd);
            Directory.Delete(_gameDir, true);
        }

        [Fact]
        public void ReplacingTheSourceUpdatesClipAndCache()
        {
            CopyDirectory(ReferenceGame.Directory, _gameDir);
            GameProject project = GameProject.Load(Path.Combine(_gameDir, "Game.agf"), new ConsoleEditorHost(TextWriter.Null));
            var clips = project.Game.RootAudioClipFolder.GetAllAudioClipsFromAllSubFolders();
            AudioClip target = clips.First(c => c.FileType == AudioClipFileType.OGG);
            AudioClip other = clips.First(c => c != target && c.FileType == AudioClipFileType.OGG &&
                File.ReadAllBytes(Path.Combine(_gameDir, ProjectPaths.ToLocal(c.CacheFileName))).Length !=
                File.ReadAllBytes(Path.Combine(_gameDir, ProjectPaths.ToLocal(target.CacheFileName))).Length);

            string newSource = Path.Combine(_gameDir, "Sounds", "replacement.ogg");
            Directory.CreateDirectory(Path.GetDirectoryName(newSource));
            File.Copy(Path.Combine(_gameDir, ProjectPaths.ToLocal(other.CacheFileName)), newSource);

            AudioOperations.SetAudioClipSourceFile(project, target, newSource);

            Assert.Equal("Sounds\\replacement.ogg", target.SourceFileName);
            Assert.Equal(File.ReadAllBytes(newSource), File.ReadAllBytes(Path.Combine(_gameDir, target.CacheFileName)));
            Assert.Throws<InvalidOperationException>(() =>
                AudioOperations.SetAudioClipSourceFile(project, target, Path.Combine(_gameDir, "Game.agf")));
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
