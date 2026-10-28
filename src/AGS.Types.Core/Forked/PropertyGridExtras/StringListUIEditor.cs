// ags4mac: forked from ags/Editor/AGS.Types/PropertyGridExtras/StringListUIEditor.cs.
// The WinForms drop-down (StringListUIEditorControl + IWindowsFormsEditorService) is replaced by a
// static GUI hook, matching how the other AGS UI editors delegate to the editor's GUI.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing.Design;

// Based on an idea by Ozcan Degirmenci:
// <http://www.ozcandegirmenci.com/post/2008/08/How-to-permit-multiple-selections-for-Enum-properties.aspx>

namespace AGS.Types
{
    public class StringListUIEditor : UITypeEditor
    {
        /// <summary>
        /// Shows a checked list of <paramref name="valueList"/> with the items in <paramref name="value"/>
        /// (joined by <see cref="Separators"/>[0]) checked. <paramref name="canUncheck"/> tells whether
        /// an item may be unchecked. Returns the new joined value.
        /// </summary>
        public delegate string StringListGUIType(string value, IList<string> valueList, Predicate<string> canUncheck);
        public static StringListGUIType StringListGUI;

        public static string[] Separators
        {
            get
            {
                return new string[] { ", ", "," };
            }
        }

        public override UITypeEditorEditStyle GetEditStyle(System.ComponentModel.ITypeDescriptorContext context)
        {
            return UITypeEditorEditStyle.DropDown;
        }

        /// <summary>Whether the user may uncheck the given item. Subclasses can veto.</summary>
        protected virtual bool CanUncheck(string item)
        {
            return true;
        }

        protected string EditValueHelper(ITypeDescriptorContext context, IServiceProvider provider,
            string value, IList<string> valueList)
        {
            if ((value == null) || (valueList == null))
            {
                throw new ArgumentNullException();
            }
            if (StringListGUI != null)
            {
                value = StringListGUI(value, valueList, CanUncheck);
            }
            return value;
        }

        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            return EditValueHelper(context, provider, value.ToString(), new List<string>());
        }
    }
}
