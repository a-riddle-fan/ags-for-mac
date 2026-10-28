using System.Linq;

namespace AGS.Editor
{
    /// <summary>
    /// File-name rules valid on every platform AGS projects are shared on. .NET reports only '/' and NUL as
    /// invalid on macOS, so a name like "Foo:Bar.asc" would be accepted on a Mac but break the project on
    /// Windows. Upstream code calling Path.GetInvalidFileNameChars/GetInvalidPathChars is rewritten at build
    /// time to use these Windows sets (see AgsSourceRewrite in AGS.Editor.Core.csproj).
    /// </summary>
    public static class PortablePaths
    {
        // As returned by .NET Framework on Windows.
        public static readonly char[] InvalidFileNameChars =
            Enumerable.Range(0, 32).Select(i => (char)i).Concat("\"<>|:*?\\/").ToArray();

        public static readonly char[] InvalidPathChars =
            Enumerable.Range(0, 32).Select(i => (char)i).Concat("\"<>|").ToArray();
    }
}
