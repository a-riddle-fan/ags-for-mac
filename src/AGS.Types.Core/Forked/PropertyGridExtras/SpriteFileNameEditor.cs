// ags4mac: forked from ags/Editor/AGS.Types/PropertyGridExtras/SpriteFileNameEditor.cs.
// WinForms FileNameEditor/OpenFileDialog replaced by a GUI hook taking the same file filter.
using System;
using System.ComponentModel;
using System.Drawing.Design;

namespace AGS.Types.PropertyGridExtras
{
    public class SpriteFileNameEditor : UITypeEditor
    {
        /// <summary>Shows a single-file open dialog; returns the chosen path or null if cancelled.</summary>
        public delegate string SpriteFileNameGUIType(string currentFileName, string fileFilter);
        public static SpriteFileNameGUIType SpriteFileNameGUI;

        public static string FileFilter => Constants.IMAGE_FILE_FILTER;

        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
        {
            return UITypeEditorEditStyle.Modal;
        }

        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            string fileName = SpriteFileNameGUI?.Invoke(value as string, FileFilter);
            return fileName ?? value;
        }
    }
}
