using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using AGS.Editor.Avalonia.ViewModels;

namespace AGS.Editor.Avalonia.Views
{
    /// <summary>
    /// The dock layout of the main window. The view model keeps its document list (<see cref="MainWindowViewModel.Documents"/>,
    /// <see cref="MainWindowViewModel.SelectedDocument"/>); this bridge mirrors it into the dock's document tabs and
    /// reports the active tab back.
    /// </summary>
    public partial class MainWindow
    {
        private EditorDockFactory _dockFactory;
        private MainWindowViewModel _dockedViewModel;
        private readonly Dictionary<DocumentViewModel, EditorDockDocument> _dockDocuments = new Dictionary<DocumentViewModel, EditorDockDocument>();
        private bool _syncingSelection;

        /// <summary>The current dock layout of this window.</summary>
        public IRootDock DockLayout => DockHost.Layout as IRootDock;

        /// <summary>The dock factory (layout model) of this window.</summary>
        public EditorDockFactory DockFactory => _dockFactory;

        private void SetUpDock(MainWindowViewModel vm)
        {
            if (ReferenceEquals(vm, _dockedViewModel)) return;
            if (_dockedViewModel != null)
            {
                _dockedViewModel.Documents.CollectionChanged -= OnDocumentsChanged;
                _dockedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
            _dockedViewModel = vm;
            _dockDocuments.Clear();
            _dockFactory = new EditorDockFactory(vm);
            IRootDock layout = (_useSavedLayout ? LoadSavedLayout() : null) ?? _dockFactory.CreateLayout();
            _useSavedLayout = true;
            _dockFactory.InitLayout(layout);
            _dockFactory.ActiveDockableChanged += (s, e) => OnDockActiveChanged(e.Dockable);
            _dockFactory.FocusedDockableChanged += (s, e) => OnDockActiveChanged(e.Dockable);
            // Floating windows: their DataContext is their part of the layout, so they carry the view model in Tag
            // like the main window (see the templates in App.axaml).
            DockHost.HostWindowFactory = () => new Dock.Avalonia.Controls.HostWindow { Tag = vm, Title = Title };
            DockHost.Factory = _dockFactory;
            DockHost.Layout = layout;
            foreach (DocumentViewModel doc in vm.Documents)
                AddDockDocument(doc, vm.Documents.IndexOf(doc));
            vm.Documents.CollectionChanged += OnDocumentsChanged;
            vm.PropertyChanged += OnViewModelPropertyChanged;
            ActivateDockDocument(vm.SelectedDocument);
        }

        /// <summary>Restores the default panel arrangement (Window → Reset Layout), keeping the open documents.</summary>
        public void ResetDockLayout()
        {
            if (_dockedViewModel == null) return;
            try { File.Delete(LayoutFile); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            MainWindowViewModel vm = _dockedViewModel;
            _dockedViewModel = null;
            _useSavedLayout = false;
            SetUpDock(vm);
        }

        private bool _useSavedLayout = true;

        /// <summary>Where the panel arrangement is kept between sessions (tests point it elsewhere).</summary>
        public static string LayoutFile { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "AGS Editor", "layout.json");

        private IRootDock LoadSavedLayout()
        {
            try
            {
                if (!File.Exists(LayoutFile)) return null;
                return JsonNode.Parse(File.ReadAllText(LayoutFile)) is JsonObject json ? _dockFactory.LoadLayout(json) : null;
            }
            catch (Exception)
            {
                return null; // unreadable: default layout
            }
        }

        /// <summary>Saves the panel arrangement (called when the window closes).</summary>
        public void SaveDockLayout()
        {
            if (_dockFactory == null || DockHost.Layout is not IRootDock root) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LayoutFile));
                File.WriteAllText(LayoutFile, _dockFactory.SaveLayout(root).ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private void AddDockDocument(DocumentViewModel model, int index)
        {
            var dockable = new EditorDockDocument(model, _dockedViewModel);
            _dockDocuments[model] = dockable;
            IDock owner = _dockFactory.Documents;
            if (index >= 0 && index <= (owner.VisibleDockables?.Count ?? 0))
                _dockFactory.InsertDockable(owner, dockable, index);
            else
                _dockFactory.AddDockable(owner, dockable);
        }

        private void RemoveDockDocument(DocumentViewModel model)
        {
            if (!_dockDocuments.Remove(model, out EditorDockDocument dockable)) return;
            if (dockable.Owner is IDock owner && owner.VisibleDockables != null && owner.VisibleDockables.Contains(dockable))
                _dockFactory.RemoveDockable(dockable, collapse: owner != _dockFactory.Documents);
        }

        private void OnDocumentsChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    for (int i = 0; i < e.NewItems.Count; i++)
                        AddDockDocument((DocumentViewModel)e.NewItems[i], e.NewStartingIndex < 0 ? -1 : e.NewStartingIndex + i);
                    break;
                case NotifyCollectionChangedAction.Remove:
                    foreach (DocumentViewModel doc in e.OldItems)
                        RemoveDockDocument(doc);
                    break;
                case NotifyCollectionChangedAction.Replace:
                    // a document replaced by a fresh one (e.g. a script reloaded): same tab position
                    for (int i = 0; i < e.OldItems.Count; i++)
                    {
                        var old = (DocumentViewModel)e.OldItems[i];
                        int position = _dockDocuments.TryGetValue(old, out EditorDockDocument oldDock) && oldDock.Owner is IDock owner
                            ? owner.VisibleDockables.IndexOf(oldDock) : -1;
                        RemoveDockDocument(old);
                        AddDockDocument((DocumentViewModel)e.NewItems[i], position);
                    }
                    break;
                default: // Reset / Move
                    foreach (DocumentViewModel doc in _dockDocuments.Keys.ToList())
                        if (!_dockedViewModel.Documents.Contains(doc))
                            RemoveDockDocument(doc);
                    foreach (DocumentViewModel doc in _dockedViewModel.Documents)
                        if (!_dockDocuments.ContainsKey(doc))
                            AddDockDocument(doc, -1);
                    break;
            }
            ActivateDockDocument(_dockedViewModel.SelectedDocument);
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedDocument))
                ActivateDockDocument(_dockedViewModel.SelectedDocument);
        }

        private void ActivateDockDocument(DocumentViewModel model)
        {
            if (model == null || _syncingSelection || !_dockDocuments.TryGetValue(model, out EditorDockDocument dockable)) return;
            if (dockable.Owner is not IDock owner || ReferenceEquals(owner.ActiveDockable, dockable)) return;
            _syncingSelection = true;
            try
            {
                _dockFactory.SetActiveDockable(dockable);
                _dockFactory.SetFocusedDockable(owner, dockable);
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        private void OnDockActiveChanged(IDockable dockable)
        {
            if (_syncingSelection || dockable is not EditorDockDocument document || _dockedViewModel == null) return;
            if (ReferenceEquals(_dockedViewModel.SelectedDocument, document.Model)) return;
            _syncingSelection = true;
            try
            {
                _dockedViewModel.SelectedDocument = document.Model;
            }
            finally
            {
                _syncingSelection = false;
            }
        }
    }
}
