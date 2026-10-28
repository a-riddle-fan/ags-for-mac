using System.Text;

namespace AGS.Editor
{
    public static class EncodingNames
    {
        /// <summary>
        /// Encoding name for the XML declaration, spelled like .NET Framework (and so the Windows editor)
        /// spells it: .NET returns "windows-1252" where .NET Framework returned "Windows-1252". Matching it
        /// keeps Game.agf identical whether it was saved on Windows or macOS.
        /// </summary>
        public static string GetWebName(Encoding encoding)
        {
            string name = encoding.WebName;
            if (name.StartsWith("windows-", System.StringComparison.Ordinal))
                return "Windows-" + name.Substring("windows-".Length);
            return name;
        }
    }
}
