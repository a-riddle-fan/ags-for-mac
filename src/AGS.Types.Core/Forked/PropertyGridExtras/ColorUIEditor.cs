// ags4mac: forked from ags/Editor/AGS.Types/PropertyGridExtras/ColorUIEditor.cs.
// PaintValue (GDI+) replaced by GetSwatchColor.
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;

namespace AGS.Types
{
    public class ColorUIEditor : UITypeEditor
    {
        public delegate Color? ColorGUIType(Color? color);
        public static ColorGUIType ColorGUI;
        public static GameColorDepth ColorMode = GameColorDepth.TrueColor;

        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
        {
            // TODO: consider providing a palette chooser for Palette mode
            return ColorMode == GameColorDepth.Palette ? UITypeEditorEditStyle.None : UITypeEditorEditStyle.DropDown;
        }

        private Color ColorFromPropertyValue(Type valueType, object value)
        {
            if (valueType == typeof(Color))
                return (Color)value;
            else if (valueType == typeof(int))
                return AGSColor.ColorMapper.MapAgsColourNumberToRgbColor((int)value);
            else
                return Color.Black; // or throw?
        }

        private object ColorToPropertyValue(ITypeDescriptorContext context, Color color)
        {
            if (context.PropertyDescriptor.PropertyType == typeof(Color))
                return color;
            else if (context.PropertyDescriptor.PropertyType == typeof(int))
                return AGSColor.ColorMapper.MapRgbColorToAgsColourNumber(color);
            else
                return null;
        }

        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            Color? color = (value != null)
                ? ColorFromPropertyValue(context.PropertyDescriptor.PropertyType, value)
                : (Color?)null;

            if (ColorGUI != null)
            {
                color = ColorGUI(color);
            }

            return color.HasValue ?
                ColorToPropertyValue(context, color.Value) :
                value; // must return original input value if there were no changes
        }

        public override bool GetPaintValueSupported(ITypeDescriptorContext context)
        {
            return true;
        }

        // ags4mac: replaces PaintValue(PaintValueEventArgs), which painted with GDI+.
        // The property grid asks for the swatch color and draws it itself.
        public Color? GetSwatchColor(object value)
        {
            if (value == null)
                return null;
            return ColorFromPropertyValue(value.GetType(), value);
        }
    }
}
