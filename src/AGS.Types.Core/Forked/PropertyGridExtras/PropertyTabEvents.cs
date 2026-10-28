// ags4mac: forked from ags/Editor/AGS.Types/PropertyGridExtras/PropertyTabEvents.cs.
// WinForms PropertyTab base and tab Bitmap replaced by AgsPropertyTab.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AGS.Types
{
    public class PropertyTabEvents : AgsPropertyTab
    {

        public override string TabName
        {
            get
            {
                return "Events";
            }
        }

        public override PropertyDescriptorCollection GetProperties(object component, Attribute[] attrs)
        {
            return GetProperties(null, component, attrs);
        }

        public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object component, Attribute[] attrs)
        {
            return TypeDescriptor.GetProperties(component, new Attribute[] { new AGSEventsTabPropertyAttribute() });
        }

    }
}
