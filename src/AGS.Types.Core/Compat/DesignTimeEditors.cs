// ags4mac: minimal, UI-neutral stand-in for the design-time editor base types that live in
// System.Windows.Forms on .NET (Windows only). It keeps upstream [Editor(typeof(X), typeof(UITypeEditor))]
// attributes and editor classes compiling unchanged. The Avalonia property grid (Phase 2) resolves an
// editor by its type and calls EditValue/GetEditStyle just like the WinForms PropertyGrid does.
using System.ComponentModel;

namespace System.Drawing.Design
{
    public enum UITypeEditorEditStyle
    {
        None = 1,
        Modal = 2,
        DropDown = 3,
    }

    public class UITypeEditor
    {
        public virtual bool IsDropDownResizable => false;

        public object EditValue(IServiceProvider provider, object value)
        {
            return EditValue(null, provider, value);
        }

        public virtual object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            return value;
        }

        public UITypeEditorEditStyle GetEditStyle()
        {
            return GetEditStyle(null);
        }

        public virtual UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
        {
            return UITypeEditorEditStyle.None;
        }

        public bool GetPaintValueSupported()
        {
            return GetPaintValueSupported(null);
        }

        public virtual bool GetPaintValueSupported(ITypeDescriptorContext context)
        {
            return false;
        }
    }
}
