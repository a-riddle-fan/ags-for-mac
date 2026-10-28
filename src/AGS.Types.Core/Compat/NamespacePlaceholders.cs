// ags4mac: several upstream files carry a now-unused "using System.Windows.Forms.Design;".
// These placeholders make the namespaces exist so those files compile unchanged. They declare no
// WinForms API, so any real WinForms usage still fails to compile.
namespace System.Windows.Forms
{
    internal static class AgsCompatNamespacePlaceholder { }
}

namespace System.Windows.Forms.Design
{
    internal static class AgsCompatNamespacePlaceholder { }
}
