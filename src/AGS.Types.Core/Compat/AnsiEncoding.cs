// ags4mac: replacement for System.Text.Encoding.Default as upstream uses it.
//
// On .NET Framework/Windows, Encoding.Default is the system ANSI code page (e.g. Windows-1252), and
// AGS stores "ANSI" game text, scripts, translations and Game.agf in it. On .NET (Core) Encoding.Default
// is always UTF-8, which would garble every non-ASCII character in ANSI projects. Linked upstream sources
// are rewritten at build time (see AgsSourceRewrite in the .csproj files) to use AnsiEncoding.Default.
using System;
using System.Text;

namespace AGS.Types
{
    public static class AnsiEncoding
    {
        /// <summary>Windows "Western European" code page, the ANSI code page of most AGS projects.</summary>
        public const int DefaultCodePage = 1252;

        private static Encoding _default;

        static AnsiEncoding()
        {
            // Code pages such as Windows-1252 are not built into .NET (Core); they come from this provider.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _default = Encoding.GetEncoding(DefaultCodePage);
        }

        /// <summary>
        /// Makes legacy code pages (Windows-125x etc.) available to Encoding.GetEncoding and XML readers.
        /// Call before loading project files; it's safe to call repeatedly.
        /// </summary>
        public static void EnsureCodePagesRegistered()
        {
            // Running the static constructor is all that's needed.
        }

        /// <summary>
        /// The code page that stands in for the Windows system ANSI code page. Defaults to Windows-1252;
        /// the editor may change it (e.g. Windows-1250 for Central European projects).
        /// </summary>
        public static Encoding Default
        {
            get { return _default; }
            set { _default = value ?? throw new ArgumentNullException(nameof(value)); }
        }
    }
}
