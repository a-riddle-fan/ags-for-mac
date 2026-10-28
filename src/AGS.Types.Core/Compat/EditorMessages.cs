// ags4mac: UI-neutral replacement for MessageBox calls in AGS.Types. The host (CLI or Avalonia UI)
// installs its own handler; the default just traces.
using System.Diagnostics;

namespace AGS.Types
{
    public static class EditorMessages
    {
        public delegate void ShowWarningHandler(string message, string title);
        public static ShowWarningHandler ShowWarning = (message, title) => Trace.TraceWarning("{0}: {1}", title, message);
    }
}
