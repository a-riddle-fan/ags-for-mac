using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AGS.Native.Tests
{
    public class NativeConstantsTests
    {
        [Fact]
        public void EveryConstantOfTheUpstreamEditorResolves()
        {
            string dir = Path.GetDirectoryName(typeof(NativeConstantsTests).Assembly.Location);
            while (dir != null && !File.Exists(Path.Combine(dir, "AGS4Mac.sln")))
                dir = Path.GetDirectoryName(dir);
            string source = File.ReadAllText(Path.Combine(dir, "ags", "Editor", "AGS.Native", "NativeMethods.cpp"));
            string[] names = Regex.Matches(source, "name->Equals\\(\"([A-Z0-9_]+)\"\\)").Select(m => m.Groups[1].Value).ToArray();

            Assert.True(names.Length > 100);
            Assert.All(names, n => Assert.NotNull(NativeConstants.GetNativeConstant(n)));
        }

        [Fact]
        public void KnownValues()
        {
            Assert.Equal("acsprset.spr", NativeConstants.GetNativeConstant("SPRSET_NAME"));
            Assert.StartsWith("Adventure Creator Game File", (string)NativeConstants.GetNativeConstant("GAME_FILE_SIG"));
            // kGameVersion_Current in upstream Common/ac/game_version.h (3060316 since 1327c5213, textual overlays).
            Assert.Equal(3060316, NativeConstants.GetNativeConstant("GAME_DATA_VERSION_CURRENT"));
            Assert.Equal(90000, NativeConstants.GetNativeConstant("MAX_STATIC_SPRITES"));
            Assert.IsType<byte[]>(NativeConstants.GetNativeConstant("PASSWORD_ENC_STRING"));
            Assert.IsType<int>(NativeConstants.GetNativeConstant("OPT_GAMETEXTENCODING"));
            Assert.Null(NativeConstants.GetNativeConstant("NO_SUCH_CONSTANT"));
            Assert.Null(NativeConstants.GetNativeConstant(null));
        }
    }
}
