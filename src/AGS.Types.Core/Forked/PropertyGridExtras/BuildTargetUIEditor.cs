// ags4mac: forked from ags/Editor/AGS.Types/PropertyGridExtras/BuildTargetUIEditor.cs.
// The CheckedListBox.ItemCheck handler becomes a CanUncheck override (see StringListUIEditor).
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace AGS.Types
{
    public class BuildTargetUIEditor : StringListUIEditor
    {
        protected override bool CanUncheck(string item)
        {
            if (item == BuildTargetsInfo.DATAFILE_TARGET_NAME)
            {
                EditorMessages.ShowWarning("Data file cannot be deselected!", "Invalid selection");
                return false;
            }
            return true;
        }

        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            return base.EditValueHelper(context, provider, string.Format("{0}{1}{2}", BuildTargetsInfo.DATAFILE_TARGET_NAME,
                StringListUIEditor.Separators[0], value.ToString()),
                new List<string>(BuildTargetsInfo.GetAvailableBuildTargetNames()));
        }
    }
}
