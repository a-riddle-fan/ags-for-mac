using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AGS.Editor.Avalonia.ViewModels
{
    public enum ProjectNodeKind
    {
        Section,
        Folder,
        Item,
    }

    /// <summary>An entry of a tree node's context menu.</summary>
    public sealed class TreeAction
    {
        public TreeAction(string header, Action execute)
        {
            Header = header;
            Command = new RelayCommand(execute);
        }

        public string Header { get; }
        public ICommand Command { get; }
    }

    /// <summary>
    /// A node of the project explorer. <see cref="Payload"/> is the AGS.Types object shown in the
    /// property grid; <see cref="Folder"/> is the folder that new items go into (sections and folders).
    /// </summary>
    public sealed partial class ProjectTreeNode : ObservableObject
    {
        public ProjectTreeNode(string name, ProjectNodeKind kind, object payload = null, object folder = null)
        {
            Name = name;
            Kind = kind;
            Payload = payload;
            Folder = folder;
        }

        public string Name { get; }
        public ProjectNodeKind Kind { get; }
        public object Payload { get; }
        public object Folder { get; }
        public ProjectTreeNode Parent { get; private set; }
        public ObservableCollection<ProjectTreeNode> Children { get; } = new ObservableCollection<ProjectTreeNode>();
        public ObservableCollection<TreeAction> Actions { get; } = new ObservableCollection<TreeAction>();

        [ObservableProperty]
        private bool _isExpanded;

        public bool HasActions => Actions.Count > 0;

        /// <summary>Stable identity across rebuilds: the chain of names from the root.</summary>
        public string Path => Parent == null ? Name : Parent.Path + "/" + Name;

        /// <summary>Upstream's icon for this node (null: show <see cref="Glyph"/>).</summary>
        public global::Avalonia.Media.Imaging.Bitmap Icon => Services.TreeIcons.For(this);

        public bool HasIcon => Icon != null;

        public string Glyph => Kind switch
        {
            ProjectNodeKind.Section => "◆",
            ProjectNodeKind.Folder => "▸",
            _ => "•",
        };

        public ProjectTreeNode Add(ProjectTreeNode child)
        {
            child.Parent = this;
            Children.Add(child);
            return child;
        }

        public void AddAction(string header, Action execute)
        {
            Actions.Add(new TreeAction(header, execute));
            OnPropertyChanged(nameof(HasActions));
        }

        public override string ToString() => Name;
    }
}
