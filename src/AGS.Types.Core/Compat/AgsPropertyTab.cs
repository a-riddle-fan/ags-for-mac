// ags4mac: UI-neutral replacement for System.Windows.Forms.Design.PropertyTab.
// Upstream marks types with [PropertyTab(typeof(PropertyTabEvents), PropertyTabScope.Component)];
// the Avalonia property grid instantiates the tab type and asks it for its property list.
using System;
using System.ComponentModel;

namespace AGS.Types
{
    public abstract class AgsPropertyTab
    {
        public abstract string TabName { get; }

        public virtual PropertyDescriptorCollection GetProperties(object component)
        {
            return GetProperties(component, null);
        }

        public abstract PropertyDescriptorCollection GetProperties(object component, Attribute[] attrs);

        public virtual PropertyDescriptorCollection GetProperties(ITypeDescriptorContext context, object component, Attribute[] attrs)
        {
            return GetProperties(component, attrs);
        }
    }
}
