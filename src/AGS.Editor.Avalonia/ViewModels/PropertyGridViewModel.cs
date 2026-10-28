using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing.Design;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AGS.Editor.Avalonia.ViewModels
{
    /// <summary>
    /// Editable property grid over System.ComponentModel metadata, the model the upstream editor's
    /// WinForms PropertyGrid uses: categories, display names, descriptions, read-only flags,
    /// TypeConverters (text and standard values) and UITypeEditors (the "…" button).
    /// </summary>
    public sealed partial class PropertyGridViewModel : ObservableObject
    {
        private readonly Action<string> _reportError;
        private object _component;

        public PropertyGridViewModel(Action<string> reportError)
        {
            _reportError = reportError ?? (_ => { });
        }

        public ObservableCollection<PropertyGridEntry> Entries { get; } = new ObservableCollection<PropertyGridEntry>();

        /// <summary>"Properties" plus the extra tabs the component declares with [PropertyTab] (e.g. Events).</summary>
        public ObservableCollection<string> Tabs { get; } = new ObservableCollection<string>();

        private readonly List<AGS.Types.AgsPropertyTab> _tabs = new List<AGS.Types.AgsPropertyTab>();
        private int _selectedTabIndex;

        public bool HasTabs => Tabs.Count > 1;

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set
            {
                if (value < 0 || value == _selectedTabIndex) return;
                _selectedTabIndex = value;
                OnPropertyChanged();
                Rebuild();
            }
        }

        /// <summary>Raised after a property value was changed through the grid (name, old value).</summary>
        public event Action<string, object> PropertyChangedByUser;

        public object Component
        {
            get => _component;
            set
            {
                if (ReferenceEquals(_component, value)) return;
                _component = value;
                OnPropertyChanged();
                UpdateTabs();
                Rebuild();
            }
        }

        private void UpdateTabs()
        {
            // Keep the selected tab (e.g. Events) when switching between objects that both have it.
            string selected = _selectedTabIndex < Tabs.Count ? Tabs[_selectedTabIndex] : null;
            Tabs.Clear();
            _tabs.Clear();
            Tabs.Add("Properties");
            if (_component != null)
            {
                foreach (PropertyTabAttribute attr in TypeDescriptor.GetAttributes(_component).OfType<PropertyTabAttribute>())
                {
                    foreach (Type tabType in attr.TabClasses ?? Array.Empty<Type>())
                    {
                        if (tabType == null || !typeof(AGS.Types.AgsPropertyTab).IsAssignableFrom(tabType)) continue;
                        var tab = (AGS.Types.AgsPropertyTab)Activator.CreateInstance(tabType);
                        _tabs.Add(tab);
                        Tabs.Add(tab.TabName);
                    }
                }
            }
            int index = selected == null ? 0 : Tabs.IndexOf(selected);
            _selectedTabIndex = index < 0 ? 0 : index;
            OnPropertyChanged(nameof(SelectedTabIndex));
            OnPropertyChanged(nameof(HasTabs));
        }

        public void Rebuild()
        {
            Entries.Clear();
            if (_component == null || _component is IEnumerable && _component is not string && !(_component is ICustomTypeDescriptor))
                return;

            PropertyDescriptorCollection properties = _selectedTabIndex == 0 || _selectedTabIndex > _tabs.Count
                ? TypeDescriptor.GetProperties(_component, new Attribute[] { BrowsableAttribute.Yes })
                : _tabs[_selectedTabIndex - 1].GetProperties(null, _component, null);

            var rows = new List<PropertyRow>();
            foreach (PropertyDescriptor p in properties)
                rows.Add(PropertyRow.Create(this, p));

            foreach (var group in rows.GroupBy(r => r.Category).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                Entries.Add(new CategoryRow(group.Key));
                foreach (PropertyRow row in group.OrderBy(r => r.Name, StringComparer.Ordinal))
                    Entries.Add(row);
            }
        }

        internal object Instance => _component;

        internal void Apply(PropertyRow row, object newValue)
        {
            object old = row.Descriptor.GetValue(_component);
            try
            {
                row.Descriptor.SetValue(_component, newValue);
                PropertyChangedByUser?.Invoke(row.Descriptor.Name, old);
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                _reportError($"Invalid value for {row.Name}: {inner.Message}");
            }
            // Other properties may depend on this one (as in the WinForms grid, refresh everything).
            foreach (PropertyRow r in Entries.OfType<PropertyRow>())
                r.Refresh();
        }

        /// <summary>A UITypeEditor changed the (reference-type) value itself instead of returning a new one.</summary>
        internal void ChangedInPlace(PropertyRow row)
        {
            PropertyChangedByUser?.Invoke(row.Descriptor.Name, row.Descriptor.GetValue(_component));
            foreach (PropertyRow r in Entries.OfType<PropertyRow>())
                r.Refresh();
        }

        internal void ReportError(string message) => _reportError(message);
    }

    public abstract class PropertyGridEntry : ObservableObject
    {
        public abstract string Name { get; }
    }

    public sealed class CategoryRow : PropertyGridEntry
    {
        public CategoryRow(string name) { Name = name; }
        public override string Name { get; }
    }

    public abstract partial class PropertyRow : PropertyGridEntry
    {
        protected PropertyRow(PropertyGridViewModel grid, PropertyDescriptor descriptor)
        {
            Grid = grid;
            Descriptor = descriptor;
            UITypeEditor editor = null;
            try
            {
                editor = descriptor.GetEditor(typeof(UITypeEditor)) as UITypeEditor;
            }
            catch (Exception)
            {
                // Editors whose type can't be created are treated as absent.
            }
            Editor = editor;
        }

        protected PropertyGridViewModel Grid { get; }
        public PropertyDescriptor Descriptor { get; }
        protected UITypeEditor Editor { get; }

        public override string Name => Descriptor.DisplayName;
        public string Category => string.IsNullOrEmpty(Descriptor.Category) ? "Misc" : Descriptor.Category;
        public string Description => Descriptor.Description;
        public bool IsReadOnly => Descriptor.IsReadOnly;
        public bool IsEditable => !Descriptor.IsReadOnly;

        /// <summary>True if a custom editor (UITypeEditor) offers a modal or drop-down UI.</summary>
        /// Read-only reference-type properties keep a modal editor, which edits the value in place (as the WinForms
        /// grid does for e.g. Room.Messages).
        public bool HasEditorButton => Editor != null &&
            (!IsReadOnly ? Editor.GetEditStyle(new DescriptorContext(Grid.Instance, Descriptor)) != UITypeEditorEditStyle.None
                : !Descriptor.PropertyType.IsValueType && Descriptor.PropertyType != typeof(string) &&
                  Editor.GetEditStyle(new DescriptorContext(Grid.Instance, Descriptor)) == UITypeEditorEditStyle.Modal);

        protected object RawValue
        {
            get
            {
                try { return Descriptor.GetValue(Grid.Instance); }
                catch (Exception) { return null; }
            }
        }

        public virtual void Refresh() => OnPropertyChanged(string.Empty);

        [RelayCommand]
        private void OpenEditor()
        {
            if (Editor == null) return;
            try
            {
                object current = RawValue;
                object edited = Editor.EditValue(new DescriptorContext(Grid.Instance, Descriptor), null, current);
                if (!Equals(edited, current))
                    Grid.Apply(this, edited);
                else if (current != null && current is not string && !current.GetType().IsValueType)
                    Grid.ChangedInPlace(this); // edited in place (room messages, custom properties): upstream ItemModified
            }
            catch (Exception ex)
            {
                Grid.ReportError($"{Name}: {ex.Message}");
            }
        }

        internal static PropertyRow Create(PropertyGridViewModel grid, PropertyDescriptor p)
        {
            Type type = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;
            if (type == typeof(bool))
                return new BoolPropertyRow(grid, p);
            TypeConverter converter = p.Converter;
            var context = new DescriptorContext(grid.Instance, p);
            // Collections are shown as "(Collection)" text (with the "…" button when they have an editor, e.g.
            // Room.Messages), as in the WinForms grid; their converters (e.g. ReferenceConverter for IList<T>)
            // would otherwise offer bogus choices.
            if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
                return new TextPropertyRow(grid, p);
            if (converter != null && converter.GetStandardValuesSupported(context))
            {
                ICollection values = null;
                try { values = converter.GetStandardValues(context); } catch (Exception) { }
                if (values != null && values.Count > 0)
                    return new ChoicePropertyRow(grid, p, values.Cast<object>().ToList(), converter.GetStandardValuesExclusive(context));
            }
            return new TextPropertyRow(grid, p);
        }
    }

    /// <summary>Text editing through the property's TypeConverter (numbers, strings, sizes, colors…).</summary>
    public sealed class TextPropertyRow : PropertyRow
    {
        public TextPropertyRow(PropertyGridViewModel grid, PropertyDescriptor descriptor) : base(grid, descriptor) { }

        /// <summary>Read-only values are shown dimmed, as in the WinForms property grid.</summary>
        public double TextOpacity => CanEditText ? 1.0 : 0.55;

        private bool IsCollection => Descriptor.PropertyType != typeof(string) && typeof(IEnumerable).IsAssignableFrom(Descriptor.PropertyType);

        public bool CanEditText => !IsCollection && IsEditable && Descriptor.Converter != null &&
            (Descriptor.PropertyType == typeof(string) || Descriptor.Converter.CanConvertFrom(new DescriptorContext(Grid.Instance, Descriptor), typeof(string)));

        public string Text
        {
            get
            {
                object value = RawValue;
                if (value == null) return string.Empty;
                if (IsCollection) return "(Collection)";
                try { return Descriptor.Converter?.ConvertToString(new DescriptorContext(Grid.Instance, Descriptor), value) ?? value.ToString(); }
                catch (Exception) { return value.ToString(); }
            }
            set
            {
                if (!CanEditText || value == Text) return;
                object converted;
                try
                {
                    converted = Descriptor.PropertyType == typeof(string)
                        ? value
                        : Descriptor.Converter.ConvertFromString(new DescriptorContext(Grid.Instance, Descriptor), value);
                }
                catch (Exception ex)
                {
                    Grid.ReportError($"Invalid value for {Name}: {(ex.InnerException ?? ex).Message}");
                    Refresh();
                    return;
                }
                Grid.Apply(this, converted);
            }
        }
    }

    public sealed class BoolPropertyRow : PropertyRow
    {
        public BoolPropertyRow(PropertyGridViewModel grid, PropertyDescriptor descriptor) : base(grid, descriptor) { }

        public bool Value
        {
            get => RawValue is bool b && b;
            set
            {
                if (IsReadOnly || value == Value) return;
                Grid.Apply(this, value);
            }
        }
    }

    /// <summary>Drop-down of the converter's standard values (enums, views, fonts, …).</summary>
    public sealed class ChoicePropertyRow : PropertyRow
    {
        public ChoicePropertyRow(PropertyGridViewModel grid, PropertyDescriptor descriptor, IReadOnlyList<object> values, bool exclusive)
            : base(grid, descriptor)
        {
            Choices = values.Select(v => new Choice(v, ToText(v))).ToList();
            Exclusive = exclusive;
        }

        public IReadOnlyList<Choice> Choices { get; }
        public bool Exclusive { get; }

        public Choice Selected
        {
            get
            {
                object value = RawValue;
                return Choices.FirstOrDefault(c => Equals(c.Value, value)) ?? Choices.FirstOrDefault(c => c.Text == ToText(value));
            }
            set
            {
                if (value == null || IsReadOnly || Equals(value.Value, RawValue)) return;
                Grid.Apply(this, value.Value);
            }
        }

        private string ToText(object value)
        {
            if (value == null) return string.Empty;
            try { return Descriptor.Converter?.ConvertToString(new DescriptorContext(Grid.Instance, Descriptor), value) ?? value.ToString(); }
            catch (Exception) { return value.ToString(); }
        }

        public sealed class Choice
        {
            public Choice(object value, string text) { Value = value; Text = text; }
            public object Value { get; }
            public string Text { get; }
            public override string ToString() => Text;
        }
    }

    /// <summary>ITypeDescriptorContext for converters and UITypeEditors, as the WinForms grid provides.</summary>
    internal sealed class DescriptorContext : ITypeDescriptorContext
    {
        public DescriptorContext(object instance, PropertyDescriptor descriptor)
        {
            Instance = instance;
            PropertyDescriptor = descriptor;
        }

        public IContainer Container => null;
        public object Instance { get; }
        public PropertyDescriptor PropertyDescriptor { get; }
        public void OnComponentChanged() { }
        public bool OnComponentChanging() => true;
        public object GetService(Type serviceType) => null;
    }
}
