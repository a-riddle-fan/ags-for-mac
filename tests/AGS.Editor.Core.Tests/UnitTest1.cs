using System.IO;

namespace AGS.Editor.Core.Tests
{
    public class SmokeTests
    {
        // The src/ projects compile upstream sources in place, so the ags/ checkout must sit next to them.
        [Fact]
        public void UpstreamCheckoutIsPresent()
        {
            string dir = Path.GetDirectoryName(typeof(SmokeTests).Assembly.Location);
            while (dir != null && !File.Exists(Path.Combine(dir, "AGS4Mac.sln")))
                dir = Path.GetDirectoryName(dir);

            Assert.NotNull(dir);
            Assert.True(Directory.Exists(Path.Combine(dir, "ags", "Editor", "AGS.Types")));
        }
    }
}
